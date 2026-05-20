using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

using System;
using Vintagestory.API.Util;
using Vintagestory.API;

#nullable enable

namespace Vintagestory.GameContent
{
    public enum EnumPiePartType
    {
        Crust, Filling, Topping
    }

    /// <summary>
    /// Defines the type of ingredient (crust, filling, topping), food
    /// category, and what mixing codes it can be used with, if any.
    /// </summary>
    public class InPieProperties
    {
        public required AssetLocation Texture;

        /// <summary>
        /// Is this filling allowed to mix with other ingredients?
        /// Crusts and toppings ignore this.
        /// <br/><br/>
        /// If false, MixingCodes has no effect because this ingredient
        /// cannot be combined with anything else.
        /// </summary>
        public bool AllowMixing = true;

        /// <summary>
        /// Is this a crust, filling, or topping?
        /// <br/><br/>
        /// * Crust is used as the pie's base. The ingredient used to
        ///   create a pie must be a crust. Crust can also be used
        ///   as a topping and is not restricted by mixing codes.
        /// <br/>
        /// * Filling is what's inside the pie. AllowMixing determines
        ///   if it can only be used for single-ingredient pies. If it
        ///   can be mixed, other ingredients must have a matching
        ///   mixing code.
        /// <br/>
        /// * Topping is the pie's last layer. Like filling, it is
        ///   restricted by mixing codes. Any crust may be used in place
        ///   of a topping without being restricted by mixing codes.
        /// </summary>
        public EnumPiePartType PartType;

        /// <summary>
        /// The food category of the ingredient when used in a pie.
        /// <br/><br/>
        /// Checks in order, stopping once a value is found:
        /// <br/><br/>
        ///   1. If stack is null, default to vegetable
        /// <br/>
        ///   2. InPieProperties.FoodCategory
        /// <br/>
        ///   3. If stack has ContainableProps:
        /// <br/>
        ///     3a. NutritionPropsPerLitreWhenInMeal.FoodCategory
        /// <br/>
        ///     3b. NutritionPropsPerLitre.FoodCategory
        /// <br/>
        ///   4. If stack has NutritionProps:
        /// <br/>
        ///     4a. NutritionPropsWhenInMeal.FoodCategory
        /// <br/>
        ///     4b. NutritionProps.FoodCategory
        /// <br/>
        ///   5. Default to vegetable
        /// <br/><br/>
        /// Note that the field default is never used when created by ReadFrom, which
        /// has its own default value.
        /// </summary>
        public EnumFoodCategory FoodCategory = EnumFoodCategory.Unknown;

        /// <summary>
        /// A list of mixing codes that are allowed for this ingredient. When
        /// checking for mixing codes, the first mixing code present in all
        /// ingredients is used for the pie type.
        /// <br/><br/>
        /// If UseFoodCategoryMixingCode is true and the ingredient does
        /// not already include the code for its food category, it will
        /// be prepended to the front. This means that by default, mixing codes
        /// are of a lower priority than the food category:
        /// <br/>
        /// [ "potpie" ] -> [ "vegetable", "potpie" ]
        /// <br/><br/>
        /// By including the food category in the list of mixing codes, other codes
        /// can be given a higher priority. For example, the following would create
        /// a "mushroom" mixing code that would take precedence over "vegetable".
        /// <br/>
        /// [ "mushroom", "vegetable", "potpie" ]
        /// <br/><br/>
        /// To disable the food category code entirely, see UseFoodCategoryMixingCode.
        /// If MixingCodes is empty, the food category code will always be added.
        /// </summary>
        public string[] MixingCodes = [];

        /// <summary>
        /// If disabled, prevents the food category tag from being added if it isn't
        /// present. The ingredient will not be usable in generic pies for its food
        /// category, instead only being usable with its specific mixing codes.
        /// 
        /// If not present in the pie properties, tries to use the object's nutritionPropsWhenInMeal.
        /// <br/><br/>
        /// If MixingCodes is empty, the food category code will always be added.
        /// </summary>
        public bool UseFoodCategoryMixingCode = true;

        /// <summary>
        /// Read pie properties from Attributes
        /// </summary>
        /// <returns>Null if "inPieProperties" is malformed or does not exist.</returns>
        public static InPieProperties? ReadFrom(CollectibleObject? obj)
        {
            if (obj?.Attributes?["inPieProperties"]?.AsObject<InPieProperties>(null, obj.Code.Domain) is not InPieProperties props) return null;

            // Get the food category manually. It doesn't need to be present in the pie properties,
            // but making the field nullable is unnecessary after parsing.
            if (props.FoodCategory == EnumFoodCategory.Unknown)
            {
                EnumFoodCategory? foodCat = null;

                if (BlockLiquidContainerBase.GetContainableProps(obj) is WaterTightContainableProps liquidProps)
                {
                    foodCat ??= liquidProps.NutritionPropsPerLitreWhenInMeal?.FoodCategory;
                    foodCat ??= liquidProps.NutritionPropsPerLitre?.FoodCategory;
                }

                foodCat ??= obj?.Attributes?["nutritionPropsWhenInMeal"]?.AsObject<FoodNutritionProperties>()?.FoodCategory;
                foodCat ??= obj?.GetNutritionProperties(null, null, null)?.FoodCategory;

                props.FoodCategory = foodCat ?? EnumFoodCategory.Unknown;
            }

            string foodCatCode = props.FoodCategory.ToString().ToLowerInvariant();
            bool missingFoodCatCode = props.UseFoodCategoryMixingCode && !props.MixingCodes.Contains(foodCatCode);
            if (props.MixingCodes.Length == 0 || missingFoodCatCode)
            {
                props.MixingCodes = props.MixingCodes.Prepend(foodCatCode).ToArray();
            }

            return props;
        }

        /// <summary>
        /// Read pie properties from ItemAttributes
        /// </summary>
        /// <returns>Null if "inPieProperties" is malformed or does not exist.</returns>
        public static InPieProperties? ReadFrom(ItemStack? stack)
        {
            return ReadFrom(stack?.Collectible);
        }
    }

    /// <summary>
    /// A single-slot inventory that hold a BlockPie ItemStack. The pie itemstack
    /// is a container with 6 slots. This makes it easy to convert it to a normal
    /// pie ItemStack.
    /// 
    /// [0]: Base dough
    /// [1-4]: Filling
    /// [5]: Crust dough
    /// 
    /// The number of content stacks is always 6. Unused slots are represented as null.
    /// </summary>
    public class BlockEntityPie : BlockEntityContainer
    {
        InventoryGeneric inv;
        public override InventoryBase Inventory => inv;

        public override string InventoryClassName => "pie";

        public BlockPie? PieBlock
        {
            get
            {
                return inv[0].Itemstack?.Block as BlockPie;
            }
        }

        public bool HasAnyFilling
        {
            get
            {
                if (PieBlock?.GetContents(Api.World, inv[0].Itemstack) is not ItemStack?[] cStacks) return false;
                return cStacks[1] != null || cStacks[2] != null || cStacks[3] != null || cStacks[4] != null;
            }
        }

        public bool HasAllFilling
        {
            get
            {
                if (PieBlock?.GetContents(Api.World, inv[0].Itemstack) is not ItemStack?[] cStacks) return false;
                return cStacks[1] != null && cStacks[2] != null && cStacks[3] != null && cStacks[4] != null;
            }
        }

        /// <summary>
        /// If this pie's topping exists, is it a Topping or a Crust?
        /// </summary>
        public EnumPiePartType? ToppingType
        {
            get
            {
                return InPieProperties.ReadFrom(PieBlock?.GetContents(Api.World, inv[0].Itemstack)?[5])?.PartType;
            }
        }

        public string? State => PieBlock?.State;



        MealMeshCache? ms;
        MeshData? mesh;

        public BlockEntityPie() : base()
        {
            inv = new InventoryGeneric(1, null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            ms = api.ModLoader.GetModSystem<MealMeshCache>();

            loadMesh();
        }

        protected override void OnTick(float dt)
        {
            base.OnTick(dt);

            if (inv[0].Itemstack?.Collectible.Code.Path == "rot")
            {
                Api.World.BlockAccessor.SetBlock(0, Pos);
                Api.World.SpawnItemEntity(inv[0].Itemstack, Pos.ToVec3d().Add(0.5, 0.1, 0.5));
            }
        }

        public override void OnBlockPlaced(ItemStack? byItemStack = null)
        {
            if (byItemStack != null)
            {
                inv[0].Itemstack = byItemStack.Clone();
                inv[0].Itemstack!.StackSize = 1;
            }
        }

        public int SlicesLeft => inv[0].Itemstack?.Attributes.GetAsInt("pieSize") ?? 0;

        /// <summary>
        /// Take a slice from the pie and set pieSize and quantityServings.
        /// 
        /// Once sliced, a pie is no longer bakeable.
        /// </summary>
        public ItemStack? TakeSlice()
        {
            if (inv[0].Itemstack?.Clone() is not ItemStack stack) return null;

            ItemStack? outStack = BlockPie.TakeSlice(ref stack!);

            if (stack == null)
            {
                Api.World.BlockAccessor.SetBlock(0, Pos);
            }

            loadMesh();
            MarkDirty(true);

            return outStack;
        }

        public void OnPlaced(IPlayer? byPlayer)
        {
            if (byPlayer?.InventoryManager.ActiveHotbarSlot.TakeOut(2) is not ItemStack doughStack) return;

            ItemStack pie = new(Block);
            (pie.Block as BlockPie)?.SetContents(pie, [doughStack, null, null, null, null, null]);
            pie.Attributes.SetInt("pieSize", 4);
            pie.Attributes.SetBool("bakeable", false);
            if (State != "raw" && !pie.Attributes.HasAttribute("quantityServings"))
            {
                pie.Attributes.SetFloat("quantityServings", pie.Attributes.GetAsInt("pieSize") * 0.25f);
            }
            inv[0].Itemstack = pie;

            loadMesh();
        }

        public bool OnInteract(IPlayer byPlayer)
        {
            if (inv[0].Itemstack?.Block is not BlockPie pieBlock) return false;

            ItemSlot? hotbarSlot = byPlayer.InventoryManager.ActiveHotbarSlot;

            if (hotbarSlot?.Itemstack?.Collectible.GetTool(hotbarSlot) is EnumTool tool && (tool is EnumTool.Knife || tool is EnumTool.Sword))
            {
                if (pieBlock.State != "raw")
                {
                    if (Api.Side == EnumAppSide.Server && TakeSlice() is ItemStack slicestack)
                    {
                        hotbarSlot.Itemstack.Collectible.DamageItem(byPlayer.Entity.World, byPlayer.Entity, hotbarSlot);
                        if (!byPlayer.InventoryManager.TryGiveItemstack(slicestack))
                        {
                            Api.World.SpawnItemEntity(slicestack, Pos);
                        }
                        Api.World.Logger.Audit("{0} Took 1x{1} slice from Pie at {2}.",
                            byPlayer.PlayerName,
                            slicestack.Collectible.Code,
                            Pos
                        );
                    }

                }
                else if (ToppingType == EnumPiePartType.Crust)
                {
                    // Cycle top crust type
                    ItemStack?[] cStacks = pieBlock.GetContents(Api.World, inv[0].Itemstack);
                    if (!HasAnyFilling || cStacks[5] == null) return true;

                    inv[0].Itemstack = BlockPie.CycleTopCrustType(inv[0].Itemstack);
                    MarkDirty(true);
                }

                return true;
            }

            // Filling rules:
            // 1. get inPieProperties
            // 2. any filing there yet? if not, all good
            // 3. Is full: Can't add more.
            // 3. If partially full, must
            //    a.) be of same foodcat
            //    b.) have props.AllowMixing set to true

            // If the pie can be picked up into the current hotbar slot,
            // skip trying to add the held stack as filling. Prevents
            // the cannot be added to pies" error message.
            bool canPickUpIntoHand = hotbarSlot?.Empty == false && inv[0].Itemstack?.Collectible.GetMergableQuantity(hotbarSlot.Itemstack, inv[0].Itemstack, EnumMergePriority.DirectMerge) > 0;

            if (hotbarSlot?.Empty == false && !canPickUpIntoHand && pieBlock.State == "raw")
            {
                bool added = TryAddIngredientFrom(hotbarSlot, byPlayer);
                if (added)
                {
                    loadMesh();
                    MarkDirty(true);
                }

                inv[0].Itemstack?.Attributes.SetBool("bakeable", HasAllFilling);

                return added;
            }

            if (SlicesLeft == 1 && inv[0].Itemstack?.Attributes.HasAttribute("quantityServings") != true)
            {
                inv[0].Itemstack?.Attributes.SetBool("bakeable", false);
                inv[0].Itemstack?.Attributes.SetFloat("quantityServings", 0.25f);
            }

            if (byPlayer.Entity.Controls.ShiftKey)
            {
                return false;
            }

            if (Api.Side == EnumAppSide.Server)
            {
                if (!byPlayer.InventoryManager.TryGiveItemstack(inv[0].Itemstack))
                {
                    Api.World.SpawnItemEntity(inv[0].Itemstack, Pos.ToVec3d().Add(0.5, 0.25, 0.5));
                }
                Api.World.Logger.Audit("{0} Took 1x{1} at {2}.",
                    byPlayer.PlayerName,
                    inv[0].Itemstack?.Collectible.Code,
                    Pos
                );
                inv[0].Itemstack = null;
            }

            Api.World.BlockAccessor.SetBlock(0, Pos);

            return true;
        }

        /// <summary>
        /// CanAddIngredient without error handling for a raw "yes or no" answer
        /// </summary>
        /// <param name="stack"></param>
        /// <returns></returns>
        public bool CanAddIngredient(ItemStack? stack)
        {
            return CanAddIngredient(stack, out _, out _, out _);
        }

        /// <summary>
        /// Check if the given ItemStack can be added to this pie.
        /// <br/><br/>
        /// Does not add the ingredient. See <see cref="TryAddIngredientFrom" />
        /// </summary>
        /// <param name="stack">The item to add to the pie.</param>
        /// <param name="emptySlotIndex">If the ingredient can be added, the slot to which is would be placed in.</param>
        /// <param name="errCode">If the ingredient cannot be added, the error code describing what went wrong.</param>
        /// <param name="errMessage">If the ingredient cannot be added, the localized error message to display.</param>
        /// <returns>True if the stack can be added to the pie.</returns>
        public bool CanAddIngredient(ItemStack? stack, out int? emptySlotIndex, out string? errCode, out string? errMessage)
        {
            errCode = null;
            errMessage = null;
            emptySlotIndex = null;

            if (InPieProperties.ReadFrom(stack) is not InPieProperties pieProps)
            {
                errCode = "notpieable";
                errMessage = Lang.Get("This item can not be added to pies");
                return false;
            }

            // Not null if pieProps exists
            if (stack!.StackSize < 2)
            {
                errCode = "notenoughingredients";
                errMessage = Lang.Get("Need at least 2 items each");
                return false;
            }

            if (inv[0].Itemstack?.Block is not BlockPie pieBlock) return false;

            ItemStack?[] cStacks = pieBlock.GetContents(Api.World, inv[0].Itemstack);

            // Special case:
            // Using a knife or crust on a pie with a crust topping should succeed without an error,
            // but the emptySlotIndex is still null because we aren't actually adding anything.
            if (ToppingType != null)
            {
                bool addingCrust = pieProps.PartType == EnumPiePartType.Crust;
                EnumTool? tool = stack.Collectible.GetTool(new DummySlot(stack));
                bool usingCuttingTool = tool == EnumTool.Knife || tool == EnumTool.Sword;

                if (ToppingType == EnumPiePartType.Crust && (addingCrust || usingCuttingTool))
                {
                    return true;
                }
                else
                {
                    errCode = "piefinished";
                    errMessage = Lang.Get("piemaking-alreadycomplete");
                    return false;
                }
            }

            if (HasAllFilling)
            {
                if (pieProps.PartType == EnumPiePartType.Filling)
                {
                    errCode = "piefullfilling";
                    errMessage = Lang.Get("Can't add more filling - already completely filled pie");
                    return false;
                }
                else if (pieProps.PartType == EnumPiePartType.Crust)
                {
                    emptySlotIndex = 5;
                    return true;
                }
            }

            if (!HasAllFilling && pieProps.PartType != EnumPiePartType.Filling)
            {
                errCode = "pieneedsfilling";
                errMessage = Lang.Get("Need to add a filling next");
                return false;
            }

            if (!HasAnyFilling)
            {
                emptySlotIndex = 1;
                return true;
            }

            InPieProperties?[] stackPieProps = cStacks.Select(InPieProperties.ReadFrom).ToArray();

            bool singleIngredient = true;
            bool allowMixing = pieProps.AllowMixing;
            IEnumerable<string> mixCodes = pieProps.MixingCodes;

            // Note that we check the topping slot here because non-crust toppings are restricted by mixing codes.
            for (int i = 1; i < cStacks.Length; i++)
            {
                if (cStacks[i] == null) break;

                singleIngredient &= cStacks[i]!.Equals(Api.World, stack, GlobalConstants.IgnoredStackAttributes);
                allowMixing &= stackPieProps[i]!.AllowMixing == true || pieProps.PartType == EnumPiePartType.Topping;
                mixCodes = stackPieProps[i]!.MixingCodes.Intersect(mixCodes) ?? [];

                if (!singleIngredient && !mixCodes.Any()) break;
            }

            if (!mixCodes.Any())
            {
                if (pieProps.PartType == EnumPiePartType.Filling)
                {
                    errCode = "piemismatchedmix";
                    errMessage = Lang.Get("piemaking-unabletomixingredient");
                }
                else
                {
                    errCode = "piemismatchedtopping";
                    errMessage = Lang.Get("piemaking-unabletoaddtopping");
                }
                return false;
            }
            else if (!singleIngredient && !allowMixing)
            {
                errCode = "pienonmixable";
                errMessage = Lang.Get("piemaking-mixingnotallowed");
                return false;
            }

            if (cStacks[4] != null) emptySlotIndex = 5;
            else if (cStacks[3] != null) emptySlotIndex = 4;
            else if (cStacks[2] != null) emptySlotIndex = 3;
            else emptySlotIndex = 2;

            return true;
        }

        private bool TryAddIngredientFrom(ItemSlot slot, IPlayer? byPlayer = null)
        {
            ICoreClientAPI? capi = byPlayer != null ? Api as ICoreClientAPI : null;

            if (inv[0].Itemstack?.Block is not BlockPie pieBlock) return false;

            if (!CanAddIngredient(slot.Itemstack, out int? emptySlotIndex, out string? errCode, out string? errMessage))
            {
                capi?.TriggerIngameError(this, errCode, errMessage);
                return false;
            }

            ItemStack[] cStacks = pieBlock.GetContents(Api.World, inv[0].Itemstack);

            if (InPieProperties.ReadFrom(slot.Itemstack)!.PartType == EnumPiePartType.Crust)
            {
                if (emptySlotIndex == null)
                {
                    // Using a knife to cycle crust type
                    inv[0].Itemstack = BlockPie.CycleTopCrustType(inv[0].Itemstack);
                    return true;
                }

                if (emptySlotIndex == 5)
                {
                    // Crust attribute must exist to stack together
                    inv[0].Itemstack?.Attributes.SetString("topCrustType", "full");
                }
            }

            cStacks[(int)emptySlotIndex!] = slot.TakeOut(2);
            pieBlock.SetContents(inv[0].Itemstack, cStacks);

            return true;
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (inv[0].Empty) return true;
            mesher.AddMeshData(mesh);
            return true;
        }

        void loadMesh()
        {
            if (Api == null || Api.Side == EnumAppSide.Server || inv[0].Empty) return;
            mesh = ms!.GetPieMesh(inv[0].Itemstack);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            bool isRotten = MealMeshCache.ContentsRotten(inv);
            if (isRotten)
            {
                dsc.Append(Lang.Get("Rotten"));
            }
            else
            {
                dsc.Append(BlockEntityShelf.PerishableInfoCompact(Api, inv[0], 0, false));
            }
        }


        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);

            if (worldForResolving.Side == EnumAppSide.Client)
            {
                MarkDirty(true);
                loadMesh();
            }
        }

        public override void OnBlockBroken(IPlayer? byPlayer = null)
        {
            //base.OnBlockBroken(); - dont drop inventory contents, the GetDrops() method already handles pie dropping
        }
    }
}
