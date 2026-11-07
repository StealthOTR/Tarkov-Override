using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using Diz.Jobs;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private sealed class SpawnEntry {public string Id,Name;public int Stack;}
        private readonly List<SpawnEntry> spawnCatalog=new List<SpawnEntry>();
        private readonly List<SpawnEntry> spawnMatches=new List<SpawnEntry>();
        private readonly Dictionary<string,SpawnEntry> spawnById=new Dictionary<string,SpawnEntry>();
        private IEnumerator<KeyValuePair<MongoID,ItemTemplate>> spawnCatalogScan;
        private ItemFactory catalogFactory;
        private string spawnSearch="bitcoin",spawnSelectedId="",spawnQuantity="1",spawnMessage="Search for an item, select it, then spawn it during a raid.";
        private bool catalogComplete,spawnSearching,spawnBusy,spawnFir;
        private int spawnSearchIndex,spawnGeneration,spawnCompleted;
        private Coroutine spawnRoutine;
        private CancellationTokenSource spawnCancellation;
        private Task spawnLoading;

        private void TickItemCatalog()
        {
            if(!compactOpen||compactTab!=6||!Singleton<ItemFactory>.Instantiated)return;
            var factory=Singleton<ItemFactory>.Instance;
            if(catalogFactory!=factory)
            {
                if(spawnCatalogScan!=null)spawnCatalogScan.Dispose();
                catalogFactory=factory;spawnCatalog.Clear();spawnById.Clear();spawnMatches.Clear();catalogComplete=false;
                spawnCatalogScan=factory.ItemTemplates.GetEnumerator();spawnSearchIndex=0;spawnSearching=true;
            }
            if(spawnCatalogScan!=null)
            {
                for(int i=0;i<128;i++)
                {
                    if(!spawnCatalogScan.MoveNext())
                    {
                        spawnCatalogScan.Dispose();spawnCatalogScan=null;catalogComplete=true;
                        StartSpawnSearch();break;
                    }
                    var pair=spawnCatalogScan.Current;var template=pair.Value;
                    if(template==null||template._type!=NodeType.Item||template.Prefab==null||string.IsNullOrEmpty(template.Prefab.path))continue;
                    string id=pair.Key.ToString(),name=EFT.LocalizationExtensions.Localized(template.NameLocalizationKey,(string)null);
                    if(string.IsNullOrEmpty(name)||name==template.NameLocalizationKey)name=template._name;
                    var entry=new SpawnEntry{Id=id,Name=name??id,Stack=Math.Max(1,template.StackMaxSize)};
                    spawnCatalog.Add(entry);spawnById[id]=entry;
                }
            }
            if(spawnSearching)
            {
                string query=spawnSearch.Trim();
                for(int i=0;i<192&&spawnSearchIndex<spawnCatalog.Count;i++,spawnSearchIndex++)
                {
                    var entry=spawnCatalog[spawnSearchIndex];
                    if((query.Length==0||entry.Name.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||entry.Id.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0)&&spawnMatches.Count<100)
                        spawnMatches.Add(entry);
                }
                if(spawnSearchIndex>=spawnCatalog.Count&&catalogComplete)spawnSearching=false;
            }
        }
        private void StartSpawnSearch()
        {spawnMatches.Clear();spawnSearchIndex=0;spawnSearching=true;}
        private void DrawItemSpawning(int column)
        {
            if(column==0)
            {
                MenuSection("SEARCH CATALOG");
                Rect search=MenuRow();string next=DarkTextField(search,spawnSearch,160);
                MenuHover(search,"Search loaded item templates by localized item name or template ID. Includes modded items with a world prefab. Results are limited to 100; narrow your search when necessary.");
                if(next!=spawnSearch){spawnSearch=next;StartSpawnSearch();}
                MenuNote((catalogComplete?spawnCatalog.Count+" items indexed": "Indexing items… "+spawnCatalog.Count)+" | "+spawnMatches.Count+" matches"+(spawnSearching?" (searching)":""));
                foreach(var entry in spawnMatches)
                {
                    Rect row=MenuRow(36);
                    if(MenuButton(row,(spawnSelectedId==entry.Id?"✓ ":"")+entry.Name))spawnSelectedId=entry.Id;
                    MenuHover(row,entry.Name+"\nTemplate: "+entry.Id+"\nMaximum stack: "+entry.Stack);
                }
            }
            else
            {
                MenuSection("SPAWN ITEM");
                MenuNote("Template ID (select a result or paste an ID):");
                spawnSelectedId=DarkTextField(MenuRow(),spawnSelectedId,80);
                SpawnEntry selected;spawnById.TryGetValue(spawnSelectedId,out selected);
                if(selected!=null)MenuNote(selected.Name+"\nMaximum stack: "+selected.Stack);
                Rect quantity=MenuRow();MenuName(new Rect(quantity.x,quantity.y,170,quantity.height),"Quantity","Total units to spawn. Ammunition is automatically split into legal stacks. At most 100 physical stacks per request.");
                spawnQuantity=DarkTextField(new Rect(quantity.x+178,quantity.y,quantity.width-178,quantity.height),spawnQuantity,8);
                Rect fir=MenuRow();MenuName(new Rect(fir.x,fir.y,fir.width-55,fir.height),"Found in raid","Mark newly spawned items as found in raid. Off leaves them without that status.");
                spawnFir=GUI.Toggle(new Rect(fir.xMax-45,fir.y,45,fir.height),spawnFir,"",GUI.skin.toggle);
                bool previous=GUI.enabled;GUI.enabled=PracticeReady()&&!spawnBusy&&!extractRequested;
                Rect spawn=MenuRow();if(MenuButton(spawn,"Spawn near player"))BeginItemSpawn();
                MenuHover(spawn,"Load this item's assets, then create real pickupable loot in front of your player. Items are placed over several frames. Weapons are bare templates; attachments can be spawned separately.");
                GUI.enabled=previous;
                if(spawnBusy&&MenuButton(MenuRow(),"Cancel remaining spawns"))CancelItemSpawn();
                MenuSection("STATUS");MenuNote(spawnMessage);
                MenuNote("Items appear about a metre in front of your player. Leave some clear ground there. Pick them up normally; the normal raid save handles items you carry out. Cancel stops future stacks and leaves already created loot in place.");
                if(!PracticeReady())MenuNote("Enter a raid with a living local player to spawn items.");
            }
        }
        private void BeginItemSpawn()
        {
            if(!PracticeReady()||spawnBusy||extractRequested||!Singleton<ItemFactory>.Instantiated||!Singleton<ObjectsFactory>.Instantiated)return;
            int quantity;
            if(!int.TryParse(spawnQuantity,out quantity)||quantity<1||quantity>100000)
            {spawnMessage="Enter a whole quantity between 1 and 100000.";return;}
            string id=spawnSelectedId.Trim();ItemTemplate template;
            try
            {
                if(!Singleton<ItemFactory>.Instance.ItemTemplates.TryGetValue(id,out template)||template._type!=NodeType.Item||template.Prefab==null||string.IsNullOrEmpty(template.Prefab.path))
                {spawnMessage="Choose a valid item template with a world prefab.";return;}
                int stack=Math.Max(1,template.StackMaxSize);
                if(SpawnRules.StackCount(quantity,stack)>100){spawnMessage="That quantity would create more than 100 stacks. Lower the quantity.";return;}
                spawnCompleted=0;spawnBusy=true;spawnCancellation=new CancellationTokenSource();
                int generation=++spawnGeneration;
                spawnRoutine=StartCoroutine(SpawnItemRoutine(id,quantity,stack,spawnFir,world,local,generation,spawnCancellation.Token));
            }
            catch(Exception error){spawnBusy=false;spawnMessage="Could not start spawning; see log.";Logger.LogWarning(error);}
        }
        private IEnumerator SpawnItemRoutine(string id,int quantity,int stack,bool foundInRaid,GameWorld raid,Player player,int generation,CancellationToken cancellation)
        {
            yield return null;
            if(!SpawnContextValid(raid,player,generation,cancellation)){CancelItemSpawn();yield break;}
            Item prototype=null;Task loading=null;Exception failure=null;
            try
            {
                var factory=Singleton<ItemFactory>.Instance;
                prototype=factory.CreateItem(factory.NextId.ToString(),id,null);
                var resources=new List<ResourceKey>();
                var paths=new HashSet<string>();
                foreach(var part in prototype.GetAllItems())
                {
                    if(part.Prefab!=null&&!string.IsNullOrEmpty(part.Prefab.path)&&paths.Add(part.Prefab.path+"|"+part.Prefab.rcid))resources.Add(part.Prefab);
                    if(part.UsePrefab!=null&&!string.IsNullOrEmpty(part.UsePrefab.path)&&paths.Add(part.UsePrefab.path+"|"+part.UsePrefab.rcid))resources.Add(part.UsePrefab);
                }
                spawnMessage="Loading item assets…";
                loading=Singleton<ObjectsFactory>.Instance.LoadBundlesAndCreatePools(ObjectsFactory.PoolsCategory.Raid,ObjectsFactory.AssemblyType.Local,resources,JobYieldPriority.General,null,cancellation);
                spawnLoading=loading;
            }
            catch(Exception error){failure=error;}
            if(failure!=null){FinishSpawn(generation,failure);yield break;}
            // Poll on Unity's main thread. Never create or register objects from a task continuation.
            while(!loading.IsCompleted)
            {
                if(!SpawnContextValid(raid,player,generation,cancellation)){CancelItemSpawn();yield break;}
                yield return null;
            }
            if(loading.IsFaulted){FinishSpawn(generation,loading.Exception);yield break;}
            if(loading.IsCanceled||!SpawnContextValid(raid,player,generation,cancellation)){CancelItemSpawn();yield break;}
            Vector3 forward=camera!=null?Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized:player.Transform.forward;
            if(forward.sqrMagnitude<0.1f)forward=player.Transform.forward;
            Vector3 right=Vector3.Cross(Vector3.up,forward),origin=player.Position+forward*1.1f+Vector3.up*0.6f;
            int remaining=quantity,index=0;
            while(remaining>0)
            {
                if(!SpawnContextValid(raid,player,generation,cancellation)){CancelItemSpawn();yield break;}
                try
                {
                    var factory=Singleton<ItemFactory>.Instance;
                    Item item=index==0?prototype:factory.CreateItem(factory.NextId.ToString(),id,null);
                    item.StackObjectsCount=Math.Min(stack,remaining);item.SpawnedInSession=foundInRaid;
                    Vector3 position=origin+right*((index%5)-2)*0.13f+forward*(index/5)*0.12f;
                    var lootItem=raid.ThrowItem(item,player,position,Quaternion.identity,Vector3.zero,Vector3.zero,false,false,0f);
                    if(lootItem==null)throw new InvalidOperationException("The game did not create a loot object.");
                    remaining-=item.StackObjectsCount;spawnCompleted+=item.StackObjectsCount;index++;
                    spawnMessage="Spawned "+spawnCompleted+" / "+quantity+" units ("+index+" stacks).";
                }
                catch(Exception error){failure=error;}
                if(failure!=null){FinishSpawn(generation,failure);yield break;}
                yield return null;
            }
            FinishSpawn(generation,null);
        }
        private bool SpawnContextValid(GameWorld raid,Player player,int generation,CancellationToken cancellation)
        {return !cancellation.IsCancellationRequested&&generation==spawnGeneration&&PracticeReady()&&world==raid&&local==player&&!extractRequested;}
        private void FinishSpawn(int generation,Exception failure)
        {
            if(generation!=spawnGeneration)return;
            spawnBusy=false;spawnRoutine=null;
            spawnLoading=null;
            if(spawnCancellation!=null){spawnCancellation.Dispose();spawnCancellation=null;}
            if(failure!=null){spawnMessage="Spawn stopped after "+spawnCompleted+" units: "+failure.GetBaseException().Message;Logger.LogWarning(failure);}
            else spawnMessage="Finished: "+spawnCompleted+" units spawned near your player.";
            nextLoot=0;
        }
        private void CancelItemSpawn()
        {
            ++spawnGeneration;
            if(spawnLoading!=null)
            {
                // Observe eventual loader failures after cancellation without touching Unity off-thread.
                spawnLoading.ContinueWith(delegate(Task task){var observed=task.Exception;},TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously);
                spawnLoading=null;
            }
            if(spawnCancellation!=null){spawnCancellation.Cancel();spawnCancellation.Dispose();spawnCancellation=null;}
            if(spawnRoutine!=null){StopCoroutine(spawnRoutine);spawnRoutine=null;}
            if(spawnBusy)spawnMessage="Cancelled. "+spawnCompleted+" units already spawned remain in the raid.";
            spawnBusy=false;
        }
    }
}
