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
using System.Diagnostics.CodeAnalysis;

namespace Vintagestory.GameContent
{
    public enum EnumPiePartType
    {
        Crust, Filling, Topping
    }

    /// <summary>
    /// Defines the type of ingredient (crust, filling, topping), food category
    /// for mixing, and what mixing codes it can be used with, if any.
    /// </summary>
    [DocumentAsJson]
    public class InPieProperties
    {
        [DocumentAsJson("Required")]
        public required AssetLocation Texture;

        /// <summary>
        /// The shape to use if this is a topping. Must be the
        /// shape of the entire pie, including the topping.
        /// </summary>
        [DocumentAsJson("Optional")]
        public Shape? ToppingShape = null;

        /// <summary>
        /// Is this filling allowed to mix with other ingredients?
        /// Crusts and toppings ignore this, but toppings always
        /// require a matching mixing code.
        /// <br/><br/>
        /// If false, MixingCodes has no effect because this ingredient
        /// cannot be combined with anything else. Don't add mixing codes
        /// if this is disabled.
        /// </summary>
        [DocumentAsJson("Optional")]
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
        [DocumentAsJson("Required")]
        public EnumPiePartType PartType;

        /// <summary>
        /// The food category of the ingredient when used in a pie. This does
        /// not affect the actual nutrition when consumed. It is only used
        /// to determine which category mixing code to prepend in the case
        /// that the code is not already present.
        ///
        /// A pie of the NoNutrition category cannot be added to pies unless
        /// there is an explicit matching mixing code. A NoNutrition ingredient
        /// with no mixing codes is an error.
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
        ///   5. Default to NoNutrition
        /// </summary>
        [DocumentAsJson("Optional")]
        public EnumFoodCategory FoodCategory = EnumFoodCategory.NoNutrition;

        /// <summary>
        /// A list of mixing codes that are allowed for this ingredient. When
        /// checking for mixing codes, the first mixing code present in all
        /// ingredients is used for the pie type.
        /// <br/><br/>
        /// If the ingredient's food category is not NoNutrition and the category
        /// code is not already present, it will be prepended. This means that by
        /// default, mixing codes are of a lower priority than the food category:
        /// <br/>
        /// [ "potpie" ] -> [ "vegetable", "potpie" ]
        /// <br/><br/>
        /// By including the food category in the list of mixing codes, other codes
        /// can be given a higher priority. For example, the following would create
        /// a "mushroom" mixing code that would take precedence over "vegetable".
        /// <br/>
        /// [ "mushroom", "vegetable", "potpie" ]
        /// <br/><br/>
        /// An ingredient may have multiple food category mixing codes. It will be
        /// allowed in any of those mixed pies and will not affect mixing codes.
        /// If it is the only ingredient, its first mixing code will determine the
        /// pie type.
        /// <br/><br/>
        /// If MixingCodes is empty, the food category code will always be added.
        /// It is an error for MixingCodes to be empty with NoNutrition.
        /// </summary>
        [DocumentAsJson("Optional")]
        public string[] MixingCodes = [];

        /// <summary>
        /// Read pie properties from Attributes
        /// </summary>
        /// <returns>Null if "inPieProperties" is malformed or does not exist.</returns>
        public static InPieProperties? ReadFrom(CollectibleObject? obj)
        {
            if (obj?.Attributes?["inPieProperties"]?.AsObject<InPieProperties>(null, obj.Code.Domain) is not InPieProperties props) return null;

            // Get the food category manually. It doesn't need to be present in the pie properties,
            // but making the field nullable is unnecessary after parsing.
            if (props.FoodCategory == EnumFoodCategory.NoNutrition)
            {
                EnumFoodCategory? foodCat = null;

                if (BlockLiquidContainerBase.GetContainableProps(obj) is WaterTightContainableProps liquidProps)
                {
                    foodCat ??= liquidProps.NutritionPropsPerLitreWhenInMeal?.FoodCategory;
                    foodCat ??= liquidProps.NutritionPropsPerLitre?.FoodCategory;
                }

                foodCat ??= obj?.Attributes?["nutritionPropsWhenInMeal"]?.AsObject<FoodNutritionProperties>()?.FoodCategory;
                foodCat ??= obj?.GetNutritionProperties(null, null, null)?.FoodCategory;

                props.FoodCategory = foodCat ?? EnumFoodCategory.NoNutrition;
            }

            // Never add the code for NoNutrition.
            if (props.FoodCategory == EnumFoodCategory.NoNutrition) return props;

            // Add the food category code if there are no mixing codes or if it wasn't explicitly added.
            string foodCatCode = props.FoodCategory.ToString().ToLowerInvariant();
            if (props.MixingCodes.Length == 0 || !props.MixingCodes.Contains(foodCatCode))
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
    /// A single-slot inventory that hold a <see cref="BlockPie" /> ItemStack. The pie itemstack
    /// is a container with 6 slots. This makes it easy to convert it to a normal
    /// pie ItemStack.
    ///
    /// GetContents() is an ItemStack[6]. Unused slots are represented as null.
    ///
    /// [0]: Base dough
    /// [1-4]: Filling
    /// [5]: Crust dough
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



        MealMeshCache ms = null!;
        MeshData mesh = null!;

        public BlockEntityPie() : base()
        {
            inv = new InventoryGeneric(1, null, null);
        }

        [MemberNotNull(nameof(ms))]
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
            ItemStack? doughStack;
            if (byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                doughStack = byPlayer?.InventoryManager.ActiveHotbarSlot.Itemstack?.Clone();
                if (doughStack != null) doughStack.StackSize = 2;
            }
            else
            {
                doughStack = byPlayer?.InventoryManager.ActiveHotbarSlot.TakeOut(2);
            }

            if (doughStack == null) return;

            ItemStack pie = new(Block);
            (pie.Block as BlockPie)?.SetContents(pie, [doughStack, null, null, null, null, null]);

            // Copy over the transition states so that we don't make a completely fresh pie from spoiling dough
            if (doughStack.Collectible.UpdateAndGetTransitionStates(byPlayer?.Entity.World, new DummySlot(doughStack)) is TransitionState[] doughStates
                && pie.Collectible.UpdateAndGetTransitionStates(byPlayer?.Entity.World, new DummySlot(pie)) is TransitionState[] pieStates)
            {

                for (int i = 0; i < doughStates.Length; i++)
                {
                    if (doughStates[i].TransitionLevel > 0)
                    {
                        float scaledHours = pieStates[i].FreshHours + pieStates[i].TransitionHours * doughStates[i].TransitionLevel;

                        if (Api.Side.IsServer()) Api.Logger.Debug($"Scaled spoiling dough lifetime to pie; {pieStates[i].FreshHours} + {pieStates[i].TransitionHours} * {doughStates[i].TransitionLevel}");

                        pie.Collectible.SetTransitionState(pie, doughStates[i].Props.Type, scaledHours);
                    }
                    else
                    {
                        float scaledHours = doughStates[i].TransitionedHours / (pieStates[i].TransitionHours / doughStates[i].TransitionHours);

                        if (Api.Side.IsServer()) Api.Logger.Debug($"Scaled fresh dough lifetime to pie; {doughStates[i].TransitionedHours} / ({pieStates[i].TransitionHours} / {doughStates[i].TransitionHours}) = {scaledHours}");

                        pie.Collectible.SetTransitionState(pie, doughStates[i].Props.Type, scaledHours);
                    }
                }
            }

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

                    MarkDirty(true);
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
            // 1. Get inPieProperties
            // 2. Any filing there yet? If not, all good
            // 3. Is full: Can't add more.
            // 3. If partially full, must
            //    a.) be of same foodcat
            //    b.) have props.AllowMixing set to true

            // If the pie can be picked up into the current hotbar slot,
            // skip trying to add the held stack as filling. Prevents
            // the "cannot be added to pies" error message.
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

            if (InPieProperties.ReadFrom(stack) is not InPieProperties pieProps || pieProps.FoodCategory == EnumFoodCategory.NoNutrition)
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

            if (PieBlock == null) return false;

            if (!CanAddIngredient(slot.Itemstack, out int? emptySlotIndex, out string? errCode, out string? errMessage))
            {
                capi?.TriggerIngameError(this, errCode, errMessage);
                return false;
            }

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

            ItemStack ingStack;

            if (byPlayer?.WorldData.CurrentGameMode == EnumGameMode.Creative)
            {
                ingStack = slot.Itemstack!.Clone();
                ingStack.StackSize = 2;
            }
            else
            {
                ingStack = slot.TakeOut(2);
            }

            ItemStack[] cStacks = PieBlock.GetContents(Api.World, inv[0].Itemstack);
            int ingredientCount = cStacks.Where(stack => stack != null).Count();

            // Average transition states before adding
            float t = (float)1 / (1 + ingredientCount);
            if (byPlayer?.Entity.World != null
                && ingStack.Collectible.UpdateAndGetTransitionStates(byPlayer.Entity.World, new DummySlot(ingStack)) is TransitionState[] ingStates
                && PieBlock.UpdateAndGetTransitionStates(byPlayer.Entity.World, inv[0]) is TransitionState[] pieStates)
            {
                for (int i = 0; i < ingStates.Length; i++)
                {
                    float totalIngHours = ingStates[i].FreshHours + ingStates[i].TransitionHours;
                    float totalPieHours = pieStates[i].FreshHours + pieStates[i].TransitionHours;
                    float scaledIngTransitionedHours = ingStates[i].TransitionedHours / (totalIngHours / totalPieHours);

                    var avgTransitionedHours = scaledIngTransitionedHours * t + pieStates[i].TransitionedHours * (1 - t);
                    if (Api.Side.IsServer()) Api.Logger.Debug($"Averaged new ingredient: {ingStates[i].TransitionedHours / (totalIngHours / totalPieHours)} * {t} + {pieStates[i].TransitionedHours} * {1 - t}");
                    PieBlock.SetTransitionState(inv[0].Itemstack, ingStates[i].Props.Type, avgTransitionedHours);
                }
            }

            cStacks[(int)emptySlotIndex!] = ingStack;
            PieBlock.SetContents(inv[0].Itemstack, cStacks);

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
            mesh = ms!.GetPieMesh(inv[0].Itemstack)!;
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
