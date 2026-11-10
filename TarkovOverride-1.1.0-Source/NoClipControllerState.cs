using System;
using UnityEngine;
namespace SPTPracticeMenu
{
    // Snapshot before changing anything, and use the backend's supported movement control.
    internal sealed class NoClipControllerState
    {
        private readonly ICharacterController controller;
        private readonly SimpleCharacterController simple;
        private readonly Collider collider;
        private readonly bool enabled,colliderEnabled,collision,hasCollision,moveIgnored;
        internal NoClipControllerState(ICharacterController value)
        {
            if(value==null)throw new ArgumentNullException("value");
            controller=value;simple=value as SimpleCharacterController;
            enabled=value.isEnabled;collider=value.GetCollider();colliderEnabled=collider!=null&&collider.enabled;
            if(simple!=null)moveIgnored=simple.IsMoveIgnored;
            else
            {
                try{collision=value.detectCollisions;hasCollision=true;}
                catch(NotSupportedException){hasCollision=false;}
            }
        }
        internal void Suspend()
        {
            // SimpleCharacterController.detectCollisions always throws, and disabling its
            // collider alone does not stop its custom Move/depenetration implementation.
            if(simple!=null)simple.IsMoveIgnored=true;
            else if(hasCollision)controller.detectCollisions=false;
            controller.isEnabled=false;
            if(collider!=null)collider.enabled=false;
        }
        internal void Restore(Action<Exception> report)
        {
            // Each restoration is independent so one failed property cannot leave another off.
            try{if(simple!=null)simple.IsMoveIgnored=moveIgnored;}catch(Exception error){report(error);}
            try{if(hasCollision)controller.detectCollisions=collision;}catch(Exception error){report(error);}
            try{controller.isEnabled=enabled;}catch(Exception error){report(error);}
            try{if(collider!=null)collider.enabled=colliderEnabled;}catch(Exception error){report(error);}
        }
    }
}
