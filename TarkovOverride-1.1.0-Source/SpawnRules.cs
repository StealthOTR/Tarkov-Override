using System;
namespace SPTPracticeMenu
{
    public static class SpawnRules
    {
        public static int StackCount(int quantity,int maximum)
        {if(quantity<1||maximum<1)throw new ArgumentOutOfRangeException();return 1+(quantity-1)/maximum;}
    }
}
