using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Datastructures;

namespace Icebreaker
{
    public class IcebreakerModSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterEntityBehaviorClass("icebreaker", typeof(EntityBehaviorIcebreaker));
        }
    }

    public class EntityBehaviorIcebreaker : EntityBehavior
    {
        ICoreServerAPI sapi;
        long tickListenerId;

        // Position delta tracking - track actual world movement to find the real forward direction
        double prevX = double.NaN;
        double prevZ = double.NaN;
        double forwardX = 0;
        double forwardZ = 0;
        bool hasForward = false;

        public EntityBehaviorIcebreaker(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            if (entity.Api is ICoreServerAPI serverApi)
            {
                this.sapi = serverApi;
                tickListenerId = sapi.Event.RegisterGameTickListener(OnTick, 100);
            }
        }

        public override void OnEntityDespawn(EntityDespawnData reason)
        {
            if (sapi != null)
            {
                sapi.Event.UnregisterGameTickListener(tickListenerId);
            }
            base.OnEntityDespawn(reason);
        }

        private bool IsIceBlock(Block block)
        {
            if (block == null || block.Code == null) return false;
            string path = block.Code.Path;
            if (block.BlockMaterial == EnumBlockMaterial.Ice) return true;
            if (path.Contains("pumice")) return false;
            if (path.Contains("ice") || path.Contains("glacier") || path.Contains("frozen")) return true;
            return false;
        }

        private bool HasIceAtPos(BlockPos bpos)
        {
            // Check all common layers (Solid, Liquid, Snow)
            for (int layer = 0; layer <= 2; layer++)
            {
                Block block = sapi.World.BlockAccessor.GetBlock(bpos, layer);
                if (IsIceBlock(block)) return true;
            }
            return false;
        }

        private void BreakIceInRadius(BlockPos center, int radius)
        {
            for (int x = -radius; x <= radius; x++)
            {
                for (int z = -radius; z <= radius; z++)
                {
                    for (int y = -1; y <= 1; y++)
                    {
                        BlockPos bpos = center.AddCopy(x, y, z);
                        if (HasIceAtPos(bpos))
                        {
                            sapi.World.BlockAccessor.BreakBlock(bpos, null);
                        }
                    }
                }
            }
        }

        private EntityBehavior cachedAccessoriesBehavior = null;
        private bool accessoriesChecked = false;

        private bool HasMetalFigurehead()
        {
            try
            {
                // Find the rideableaccessories behavior - cache it after first lookup
                if (!accessoriesChecked)
                {
                    accessoriesChecked = true;

                    var allBehaviors = entity.SidedProperties?.Behaviors;
                    if (allBehaviors != null)
                    {
                        foreach (var b in allBehaviors)
                        {
                            if (b.GetType().Name.Contains("RideableAccessories") || b.GetType().Name.Contains("rideableaccessories"))
                            {
                                cachedAccessoriesBehavior = b;
                                break;
                            }
                        }
                    }

                    // Also try the standard lookup
                    if (cachedAccessoriesBehavior == null)
                    {
                        cachedAccessoriesBehavior = entity.GetBehavior("rideableaccessories");
                    }
                }

                if (cachedAccessoriesBehavior == null)
                {
                    return false;
                }

                // Try getting inventory via reflection
                var invProp = cachedAccessoriesBehavior.GetType().GetProperty("Inventory")
                           ?? cachedAccessoriesBehavior.GetType().GetProperty("inventory");

                if (invProp == null)
                {
                    return false;
                }

                var invObj = invProp.GetValue(cachedAccessoriesBehavior);
                if (invObj == null)
                {
                    return false;
                }

                if (invObj is Vintagestory.API.Common.IInventory inv)
                {
                    foreach (var slot in inv)
                    {
                        if (slot.Empty) continue;

                        var stack = slot.Itemstack;
                        if (stack == null) continue;

                        string codePath = stack.Collectible?.Code?.Path;
                        if (codePath != null && codePath.StartsWith("metalfigurehead"))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (System.Exception)
            {
                // Silently ignore reflection errors
            }
            return false;
        }

        private void OnTick(float dt)
        {
            if (entity.Alive && entity.Pos != null)
            {
                // Only act if the metal figurehead is attached
                if (!HasMetalFigurehead())
                {
                    return;
                }

                try
                {
                    double curX = entity.Pos.X;
                    double curZ = entity.Pos.Z;

                    // Track actual position deltas to determine real travel direction
                    if (!double.IsNaN(prevX))
                    {
                        double dx = curX - prevX;
                        double dz = curZ - prevZ;
                        double dist = System.Math.Sqrt(dx * dx + dz * dz);

                        if (dist > 0.01)
                        {
                            forwardX = dx / dist;
                            forwardZ = dz / dist;
                            hasForward = true;
                        }
                    }

                    prevX = curX;
                    prevZ = curZ;

                    // Coverage offsets along the boat length (from back to front)
                    // Sailboats are roughly 9 blocks long. We check points at -3, 0, 3, and 5 (tip).
                    double[] offsets = new double[] { -3, 0, 3, 5 };

                    foreach (double offset in offsets)
                    {
                        double checkX = curX;
                        double checkZ = curZ;

                        if (hasForward)
                        {
                            checkX += forwardX * offset;
                            checkZ += forwardZ * offset;
                        }

                        BlockPos bpos = new BlockPos((int)checkX, (int)(entity.Pos.Y + 0.1), (int)checkZ);
                        BreakIceInRadius(bpos, 2);
                    }
                }
                catch (System.Exception)
                {
                    // Suppress exceptions from unloaded chunks etc.
                }
            }
        }

        public override string PropertyName()
        {
            return "icebreaker";
        }
    }
}

