﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace StoneQuarry
{
    public class BlockStoneSlab : Block, IMultiBlockMonolithicSmall
    {
        const float HitTime = .2f;

        public static AssetLocation HitSoundLocation => new AssetLocation("game", "sounds/block/rock-hit-pickaxe");
        public static AssetLocation DropSoundLocation => new AssetLocation("game", "sounds/block/heavyice");

        public WorldInteraction[] WorldInteractions { get; private set; }
        public SimpleParticleProperties InteractParticles { get; private set; }

        public BaseAllowedCodes AllowedCodes { get; private set; }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            InteractParticles = new SimpleParticleProperties()
            {
                MinPos = new Vec3d(),
                AddPos = new Vec3d(.5, .5, .5),
                MinQuantity = 5,
                AddQuantity = 20,
                GravityEffect = .9f,
                WithTerrainCollision = true,
                ParticleModel = EnumParticleModel.Quad,
                LifeLength = 0.5f,
                MinVelocity = new Vec3f(-0.4f, -0.4f, -0.4f),
                AddVelocity = new Vec3f(0.8f, 1.2f, 0.8f),
                MinSize = 0.1f,
                MaxSize = 0.4f,
                DieOnRainHeightmap = false
            };

            InitWorldInteractions();

            AllowedCodes = new BaseAllowedCodes();
            AllowedCodes.FromJson(Attributes["allowedCodes"].ToString());
        }

        public void InitWorldInteractions()
        {
            var toolStacksByType = new Dictionary<string, List<ItemStack>>()
            {
                {"rockpolished", new List<ItemStack>()},
                {"rock", new List<ItemStack>()},
                {"stone", new List<ItemStack>()},
                {"stonebrick", new List<ItemStack>()}
            };

            foreach (var collObj in api.World.Collectibles)
            {
                if (collObj is ItemSlabTool)
                {
                    string type = (collObj as ItemSlabTool).DropType;
                    if (type != "")
                    {
                        toolStacksByType[type].Add(new ItemStack(collObj));
                    }
                }
            }

            WorldInteractions = new WorldInteraction[] {
                new WorldInteraction(){
                    ActionLangCode = Core.ModId + ":wi-stoneslab-rockpolished",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = toolStacksByType["rockpolished"].ToArray()
                },
                new WorldInteraction(){
                    ActionLangCode = Core.ModId + ":wi-stoneslab-rock",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = toolStacksByType["rock"].ToArray()
                },
                new WorldInteraction(){
                    ActionLangCode = Core.ModId + ":wi-stoneslab-stone",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = toolStacksByType["stone"].ToArray()
                },
                new WorldInteraction(){
                    ActionLangCode = Core.ModId + ":wi-stoneslab-stonebrick",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = toolStacksByType["stonebrick"].ToArray()
                },
                new WorldInteraction()
                {
                    ActionLangCode = Core.ModId + ":wi-stoneslab-changerock",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sprint"
                }
            };
        }

        public override bool DoPlaceBlock(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ItemStack byItemStack)
        {
            if (!base.DoPlaceBlock(world, byPlayer, blockSel, byItemStack)) return false;

            if (byItemStack != null)
            {
                if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEStoneSlab be)
                {
                    be.ContentFromAttributes(byItemStack.Attributes.Clone(), world);
                }
            }

            return true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemStack activeStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;

            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BEStoneSlab be)
            {
                if (byPlayer.WorldData.CurrentGameMode == EnumGameMode.Creative && activeStack != null)
                {
                    if (byPlayer.Entity.Controls.Sprint)
                    {
                        be.Inventory.TryRemoveStack(activeStack);
                        return true;
                    }
                    else if (AllowedCodes.Rocks.Contains(activeStack.Collectible.Code.ToString()))
                    {
                        be.Inventory.TryAddStack(activeStack);
                        return true;
                    }
                }

                if (!be.Inventory.Empty && activeStack?.Collectible is ItemSlabTool)
                {
                    return true;
                }

                if (byPlayer.Entity.Controls.Sprint)
                {
                    be.Inventory.NextSlot();
                    return true;
                }
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            ItemStack activeStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (activeStack?.Collectible is ItemSlabTool)
            {
                if (api.Side == EnumAppSide.Client)
                {
                    if (activeStack.Collectible.FirstCodePart() == "rubblehammer")
                    {
                        ModelTransform tf = ModelTransform.NoTransform;

                        float offset = secondsUsed / Core.Config.SlabInteractionTime;
                        tf.Translation.Set(offset * .25f, 0, offset * .5f);

                        byPlayer.Entity.Controls.UsingHeldItemTransformBefore = tf;
                    }
                    else
                    {
                        ModelTransform tf = ModelTransform.NoTransform;
                        tf.Translation.Set(secondsUsed % HitTime, 0, 0);
                        byPlayer.Entity.Controls.UsingHeldItemTransformBefore = tf;

                        int times = byPlayer.Entity.WatchedAttributes.GetInt("sq_slab_times", 1);

                        if (secondsUsed > times * HitTime)
                        {
                            InteractParticles.ColorByBlock = world.BlockAccessor.GetBlock(blockSel.Position);
                            InteractParticles.MinPos = blockSel.Position.ToVec3d() + blockSel.HitPosition;
                            world.SpawnParticles(InteractParticles, byPlayer);

                            world.PlaySoundAt(HitSoundLocation, byPlayer, byPlayer, true, 32, .5f);

                            byPlayer.Entity.WatchedAttributes.SetInt("sq_slab_times", times + 1);
                        }
                    }
                }

                return secondsUsed < Core.Config.SlabInteractionTime;
            }

            return base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel);
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (api.Side == EnumAppSide.Client)
            {
                byPlayer.Entity.WatchedAttributes.SetInt("sq_slab_times", 1);
            }


            ItemStack activeStack = byPlayer.InventoryManager.ActiveHotbarSlot.Itemstack;
            if (secondsUsed < Core.Config.SlabInteractionTime || !(activeStack.Collectible is ItemSlabTool tool))
            {
                base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
                return;
            }

            if (activeStack?.Collectible.FirstCodePart() == "rubblehammer")
            {
                InteractParticles.ColorByBlock = world.BlockAccessor.GetBlock(blockSel.Position);
                InteractParticles.MinPos = blockSel.Position.ToVec3d() + blockSel.HitPosition;
                world.SpawnParticles(InteractParticles, byPlayer);
                world.PlaySoundAt(HitSoundLocation, byPlayer, byPlayer, true, 32, .5f);
            }


            if (api.Side == EnumAppSide.Server)
            {

                var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BEStoneSlab;
                if (be != null && !be.Inventory.Empty)
                {
                    var dropStack = be.Inventory.GetContent(byPlayer, tool.DropType, tool.Quantity);
                    if (dropStack == null)
                    {
                        return;
                    }

                    var dropPos = blockSel.Position.ToVec3d() + blockSel.HitPosition;
                    var dropVel = new Vec3d(.05 * blockSel.Face.Normalf.ToVec3d().X, .1, .05 * blockSel.Face.Normalf.ToVec3d().Z);

                    world.PlaySoundAt(DropSoundLocation, byPlayer, byPlayer, true, 32, .05f);

                    world.SpawnItemEntity(dropStack, dropPos, dropVel);

                    if (be.Inventory.Empty)
                    {
                        world.BlockAccessor.BreakBlock(blockSel.Position, byPlayer);
                    }

                    activeStack.Collectible.DamageItem(world, byPlayer.Entity, byPlayer.InventoryManager.ActiveHotbarSlot, 1);
                }
            }
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            string langKey = Core.ModId + ":info-stoneslab-heldinfo(count={0},stone={1})";

            int qslots = inSlot.Itemstack.Attributes.GetInt("qslots", 0);
            if (qslots > 0)
            {
                var tree = inSlot.Itemstack.Attributes.GetTreeAttribute("slots");
                for (int i = 0; i < qslots; i++)
                {
                    var stack = tree.GetItemstack(i + "");
                    if (stack?.StackSize > 0)
                    {
                        stack.ResolveBlockOrItem(world);
                        string rock = Lang.Get(stack.Collectible.Code.ToString());
                        dsc.AppendLine(Lang.Get(langKey, stack.StackSize, rock));
                    }
                }
            }
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return WorldInteractions.Append(base.GetPlacedBlockInteractionHelp(world, selection, forPlayer));
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var be = world.BlockAccessor.GetBlockEntity(pos) as BEStoneSlab;
            var drop = be?.GetSelfDrop();

            if (drop == null)
            {
                return null;
            }

            return new[] { drop };
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            return (world.BlockAccessor.GetBlockEntity(pos) as BEStoneSlab)?.GetSelfDrop() ?? base.OnPickBlock(world, pos);
        }

        public Cuboidf[] MBGetCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return CollisionBoxes;
        }

        public Cuboidf[] MBGetSelectionBoxes(IBlockAccessor blockAccessor, BlockPos pos, Vec3i offset)
        {
            return CollisionBoxes;
        }
    }
}