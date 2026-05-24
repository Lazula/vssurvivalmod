using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

#nullable enable

namespace Vintagestory.GameContent
{
    public class PieTopCrustType
    {
        public required string Code;
        public required string ShapeElement;
    }

    // Definition: GetContents() must always return a ItemStack[] of array length 6
    // [0] = crust
    // [1-4] = filling
    // [5] = topping (unused atm)
    public class BlockPie : BlockMeal, IBakeableCallback, IShelvable
    {
        /// <summary>
        /// The pie's cooking stage; "raw", "partbaked", "perfect", or "charred"
        /// </summary>
        public string State => Variant["state"];
        protected override bool PlacedBlockEating => false;

        public EnumShelvableLayout? GetShelvableType(ItemStack stack)
        {
            return stack.Attributes.GetAsInt("pieSize") switch
            {
                1 => EnumShelvableLayout.Quadrants,
                2 => EnumShelvableLayout.Halves,
                _ => EnumShelvableLayout.SingleCenter
            };
        }
        public ModelTransform? GetOnShelfTransform(ItemStack stack)
        {
            return GetShelvableType(stack) switch
            {
                EnumShelvableLayout.Quadrants => stack.Collectible.Attributes?["onShelfQuarterTransform"].AsObject<ModelTransform>(),
                EnumShelvableLayout.Halves => stack.Collectible.Attributes?["onShelfHalfTransform"].AsObject<ModelTransform>(),
                _ => stack.Collectible.Attributes?["onShelfFullTransform"].AsObject<ModelTransform>()
            };
        }

        MealMeshCache? ms;

        WorldInteraction[]? interactions;

        public static PieTopCrustType[] TopCrustTypes = null!;

        [MemberNotNull(nameof(ms), nameof(interactions))]
        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            TopCrustTypes ??= api.Assets.Get("config/pietopcrusttypes.json").ToObject<PieTopCrustType[]>();

            InteractionHelpYOffset = 0.375f;

            interactions = ObjectCacheUtil.GetOrCreate(api, "pieInteractions-", () =>
            {
                ItemStack[] knifeStacks = ObjectCacheUtil.GetToolStacks(api, EnumTool.Knife);
                List<ItemStack> doughStacks = [];
                List<ItemStack> fillStacks = [];
                List<ItemStack> toppingStacks = [];

                if (fillStacks.Count == 0 && doughStacks.Count == 0)
                {
                    foreach (CollectibleObject obj in api.World.Collectibles)
                    {
                        EnumPiePartType? partType = InPieProperties.ReadFrom(obj)?.PartType;

                        if (obj is ItemDough || partType == EnumPiePartType.Crust)
                        {
                            doughStacks.Add(new ItemStack(obj, 2));
                        }

                        switch (partType)
                        {
                            case EnumPiePartType.Filling:
                                fillStacks.Add(new ItemStack(obj, 2));
                                break;
                            case EnumPiePartType.Topping:
                                toppingStacks.Add(new ItemStack(obj, 2));
                                break;
                            case EnumPiePartType.Crust:
                                toppingStacks.Add(new ItemStack(obj, 2));
                                break;
                        }
                    }
                }

                return new WorldInteraction[]
                {
                    new() {
                        ActionLangCode = "blockhelp-pie-cut",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = knifeStacks,
                        GetMatchingStacks = (wi, bs, _) => {
                            if (GetBlockEntity<BlockEntityPie>(bs.Position) is not BlockEntityPie pie) return null;
                            return pie.State != "raw" && pie.SlicesLeft > 1 ? wi.Itemstacks : null;
                        }
                    },
                    new() {
                        ActionLangCode = "blockhelp-pie-addfilling",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = fillStacks.ToArray(),
                        GetMatchingStacks = (wi, bs, _) => {
                            if (GetBlockEntity<BlockEntityPie>(bs.Position) is not BlockEntityPie pie) return null;
                            return pie.State == "raw" && !pie.HasAllFilling ? wi.Itemstacks.Where(pie.CanAddIngredient).ToArray() : null;
                        }
                    },
                    new() {
                        ActionLangCode = "blockhelp-pie-addcrustortopping",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = toppingStacks.ToArray(),
                        GetMatchingStacks = (wi, bs, _) => {
                            if (GetBlockEntity<BlockEntityPie>(bs.Position) is not BlockEntityPie pie) return null;
                            return pie.State == "raw" && pie.HasAllFilling && pie.ToppingType == null ? wi.Itemstacks.Where(pie.CanAddIngredient).ToArray() : null;
                        }
                    },
                    new() {
                        ActionLangCode = "blockhelp-pie-changecruststyle",
                        MouseButton = EnumMouseButton.Right,
                        Itemstacks = knifeStacks,
                        GetMatchingStacks = (wi, bs, _) => {
                            if (GetBlockEntity<BlockEntityPie>(bs.Position) is not BlockEntityPie pie) return null;
                            return pie.State == "raw" && pie.ToppingType == EnumPiePartType.Crust ? wi.Itemstacks : null;
                        }
                    }
                };
            });

            ms = api.ModLoader.GetModSystem<MealMeshCache>();

            displayContentsInfo = false;
        }


        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            if (!canEat(slot)) return;
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
        }

        public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (!canEat(slot)) return false;

            return base.OnHeldInteractStep(secondsUsed, slot, byEntity, blockSel, entitySel);
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            if (!canEat(slot)) return;

            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
        }


        protected bool canEat(ItemSlot slot)
        {
            return slot.Itemstack?.Attributes?.GetAsInt("pieSize") == 1 && State != "raw";
        }


        ModelTransform oneSliceTranformGui = new ModelTransform()
        {
            Origin = new FastVec3f(0.375f, 0.1f, 0.375f),
            Scale = 2.82f,
            Rotation = new FastVec3f(-27, 132, -5)
        }.EnsureDefaultValues();

        ModelTransform oneSliceTranformTp = new ModelTransform()
        {
            Translation = new FastVec3f(-0.82f, -0.34f, -0.57f),
            Origin = new FastVec3f(0.5f, 0.13f, 0.5f),
            Scale = 0.7f,
            Rotation = new FastVec3f(-49, 29, -112)
        }.EnsureDefaultValues();


        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            if (itemstack.Attributes.GetAsInt("pieSize") == 1)
            {
                if (target == EnumItemRenderTarget.Gui)
                {
                    renderinfo.Transform = oneSliceTranformGui;
                }
                if (target == EnumItemRenderTarget.HandTp)
                {
                    renderinfo.Transform = oneSliceTranformTp;
                }
            }

            renderinfo.ModelRef = ms!.GetOrCreatePieMeshRef(itemstack);
        }


        public override MeshData? GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos? atBlockPos = null)
        {
            return ms!.GetPieMesh(slot.Itemstack);
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            if ((world.BlockAccessor.GetBlockEntity(pos) as BlockEntityPie)?.Inventory[0].Itemstack is ItemStack pieStack) return pieStack.Clone();

            return base.OnPickBlock(world, pos);
        }




        public void OnBaked(ItemStack oldStack, ItemStack newStack)
        {
            // Copy over properties and bake the contents
            newStack.Attributes["contents"] = oldStack.Attributes["contents"];
            newStack.Attributes.SetInt("pieSize", oldStack.Attributes.GetAsInt("pieSize"));
            newStack.Attributes.SetString("topCrustType", GetTopCrustType(oldStack));
            newStack.Attributes.SetInt("bakeLevel", oldStack.Attributes.GetAsInt("bakeLevel", 0) + 1);

            ItemStack[] stacks = GetContents(api.World, newStack);

            // 1. Cook contents, if there is a cooked version of it
            /*for (int i = 0; i < stacks.Length; i++)
            {
                if (stacks[i]?.Collectible.CombustibleProps.SmeltedStack?.ResolvedItemstack?.Clone() is ItemStack cookedStack)
                {
                    ItemSlot slot = new DummySlot(cookedStack);

                    if (UpdateAndGetTransitionState(api.World, slot, EnumTransitionType.Perish) is TransitionState state && cookedStack.Collectible.UpdateAndGetTransitionState(api.World, slot, EnumTransitionType.Perish) is TransitionState smeltedState)
                    {
                        float nowTransitionedHours = state.TransitionedHours / (state.TransitionHours + state.FreshHours) * 0.8f * (smeltedState.TransitionHours + smeltedState.FreshHours) - 1;

                        cookedStack.Collectible.SetTransitionState(cookedStack, EnumTransitionType.Perish, Math.Max(0, nowTransitionedHours));
                    }

                    stacks[i] = cookedStack;
                }
            }*/// This breaks pies by causing them to have cooked meat and stuff inside which the game doesn't know how to handle.

            SetContents(newStack, stacks);
        }

        public void TryPlacePie(EntityAgent byEntity, BlockSelection blockSel)
        {
            IPlayer? byPlayer = (byEntity as EntityPlayer)?.Player;
            ItemSlot? hotbarSlot = byPlayer?.InventoryManager.ActiveHotbarSlot;

            if (InPieProperties.ReadFrom(hotbarSlot?.Itemstack)?.PartType != EnumPiePartType.Crust) return;

            BlockPos abovePos = blockSel.Position.UpCopy();

            Block atBlock = api.World.BlockAccessor.GetBlock(abovePos);
            if (atBlock.Replaceable < 6000) return;

            api.World.BlockAccessor.SetBlock(Id, abovePos);

            (api.World.BlockAccessor.GetBlockEntity(abovePos) as BlockEntityPie)?.OnPlaced(byPlayer);
        }



        public override string GetPlacedBlockName(IWorldAccessor world, BlockPos pos)
        {
            if ((world.BlockAccessor.GetBlockEntity(pos) as BlockEntityPie)?.Inventory[0]?.Itemstack is ItemStack pieStack) return GetHeldItemName(pieStack);

            return base.GetPlacedBlockName(world, pos);
        }

        public override string GetHeldItemName(ItemStack? itemStack)
        {
            ItemStack[] cStacks = GetContents(api.World, itemStack);
            if (cStacks.Length <= 1 || cStacks[1] == null) return Lang.Get("pie-empty");

            bool singleIngredient = true;
            IEnumerable<string> mixCodes = InPieProperties.ReadFrom(cStacks[1])!.MixingCodes ?? [];
            for (int i = 2; i < cStacks.Length - 1; i++)
            {
                if (cStacks[i] == null) continue;

                singleIngredient &= cStacks[i].Equals(api.World, cStacks[1], GlobalConstants.IgnoredStackAttributes);
                mixCodes = InPieProperties.ReadFrom(cStacks[i])!.MixingCodes.Intersect(mixCodes) ?? [];

                if (!singleIngredient && !mixCodes.Any()) break;
            }

            string state = Variant["state"];

            if (MealMeshCache.ContentsRotten(cStacks))
            {
                return Lang.Get("pie-single-rotten");
            }

            string pieName = Lang.Get(singleIngredient
                ? "pie-single-" + cStacks[1].Collectible.Code.ToShortString() + "-" + state
                : "pie-mixed-" + mixCodes.First() + "-" + state);

            if (cStacks[5] != null && InPieProperties.ReadFrom(cStacks[5])!.PartType != EnumPiePartType.Crust)
            {
                return Lang.Get("meal-topping-ingredient-format", cStacks[5].Collectible.GetHeldItemName(cStacks[5]), pieName.ToLowerInvariant());
            }
            else
            {
                return pieName;
            }
        }

        /// <summary>
        /// The food category of the ingredient when used in a pie.
        /// 
        /// See InPieProperties.ReadFrom()
        /// </summary>
        public static EnumFoodCategory IngredientFoodCategory(ItemStack? stack)
        {
            if (InPieProperties.ReadFrom(stack) is InPieProperties pieProps) return pieProps.FoodCategory;

            return EnumFoodCategory.Unknown;
        }


        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            if (inSlot.Itemstack is not ItemStack pieStack) return;

            int pieSize = pieStack.Attributes.GetAsInt("pieSize");
            float servingsLeft = GetQuantityServings(world, pieStack);
            if (pieStack.Attributes?.HasAttribute("quantityServings") == false) servingsLeft = pieStack.Attributes.GetAsInt("pieSize") / 4f;

            if (pieSize == 1)
            {
                dsc.AppendLine(Lang.Get("pie-slice-single", servingsLeft));
            }
            else
            {
                dsc.AppendLine(Lang.Get("pie-slices", pieSize));
            }


            TransitionableProperties[] propsm = pieStack.Collectible.GetTransitionableProperties(api.World, pieStack, null);
            if (propsm != null && propsm.Length > 0)
            {
                pieStack.Collectible.AppendPerishableInfoText(inSlot, dsc, api.World);
            }

            ItemStack[] stacks = GetContents(api.World, pieStack);

            EntityPlayer? forEntity = (world as IClientWorldAccessor)?.Player?.Entity;


            float[] nmul = GetNutritionHealthMul(null, inSlot, forEntity);
            dsc.AppendLine(GetContentNutritionFacts(api.World, inSlot, stacks, null, true, servingsLeft * nmul[0], servingsLeft * nmul[1]));
        }

        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            if ((world.BlockAccessor.GetBlockEntity(pos) as BlockEntityPie)?.Inventory is not InventoryBase bepInv) return "";
            if (bepInv.Count < 1 || bepInv[0].Itemstack is not ItemStack pieStack) return "";

            ItemStack[] stacks = GetContents(api.World, pieStack);
            StringBuilder sb = new();

            TransitionableProperties[]? propsm = pieStack.Collectible.GetTransitionableProperties(api.World, pieStack, null);
            if (propsm?.Length > 0)
            {
                pieStack.Collectible.AppendPerishableInfoText(bepInv[0], sb, api.World);
            }

            float servingsLeft = GetQuantityServings(world, pieStack);
            if (pieStack.Attributes?.HasAttribute("quantityServings") == false) servingsLeft = pieStack.Attributes.GetAsInt("pieSize") / 4f;

            float[] nmul = GetNutritionHealthMul(pos, null, forPlayer.Entity);

            string str = sb.ToString();
            str += GetContentNutritionFacts(api.World, bepInv[0], stacks, null, true, nmul[0] * servingsLeft, nmul[1] * servingsLeft) ?? "";


            return str;
        }

        // Skip over the meal transitioning code because only the pie itself spoils, not its contents.
        public override TransitionState[]? UpdateAndGetTransitionStates(IWorldAccessor world, ItemSlot inslot)
        {
            return UpdateAndGetTransitionStatesNative(world, inslot);
        }

        public override float[] GetNutritionHealthMul(BlockPos? pos, ItemSlot? slot, EntityAgent? forEntity)
        {
            float satLossMul = 1f;

            if (slot == null && pos != null)
            {
                slot = (api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityPie)?.Inventory[0];
            }

            if (slot != null)
            {
                TransitionState? state = slot.Itemstack?.Collectible.UpdateAndGetTransitionState(api.World, slot, EnumTransitionType.Perish);
                float spoilState = state?.TransitionLevel ?? 0;
                satLossMul = GlobalConstants.FoodSpoilageSatLossMul(spoilState, slot.Itemstack, forEntity);
            }

            return [Attributes["nutritionMul"].AsFloat(1) * satLossMul, satLossMul];
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            return GetBlockEntity<BlockEntityPie>(blockSel.Position)?.OnInteract(byPlayer) == true || base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override void OnBlockInteractStop(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // Don't call eating stuff from blockmeal
            //base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
        }

        /// <summary>
        /// Take a slice from the pie in the itemstack and set pieSize and quantityServings.
        /// 
        /// Once sliced, a pie is no longer bakeable.
        /// </summary>
        /// <param name="stack">The pie to take a slice from.</param>
        /// <returns>The new slice as an ItemStack.</returns>
        public static ItemStack? TakeSlice(ref ItemStack? stack)
        {
            if (stack?.Clone() is not ItemStack outStack) return null;

            int size = stack.Attributes.GetAsInt("pieSize");
            float servings = stack.Attributes.GetFloat("quantityServings");

            if (size <= 1 && !outStack.Attributes.HasAttribute("quantityServings"))
            {
                outStack.Attributes.SetFloat("quantityServings", 0.25f);
                stack = null;
            }
            else
            {
                stack.Attributes.SetInt("pieSize", size - 1);
                if (stack.Attributes.HasAttribute("quantityServings"))
                {
                    stack.Attributes.SetFloat("quantityServings", servings - 0.25f);
                }

                outStack.Attributes.SetInt("pieSize", 1);
                outStack.Attributes.SetFloat("quantityServings", 0.25f);
            }

            outStack.Attributes.SetBool("bakeable", false);

            return outStack;
        }

        public override bool MatchesForCrafting(ItemStack inputStack, IRecipeBase gridRecipe, IRecipeIngredient ingredient)
        {
            if (gridRecipe.Name != "pieslice") return base.MatchesForCrafting(inputStack, gridRecipe, ingredient);

            return inputStack?.Collectible is BlockPie && inputStack.Attributes.GetAsInt("pieSize") > 1;
        }

        public override void OnCreatedByCrafting(ItemSlot[] allInputslots, ItemSlot outputSlot, IRecipeBase byRecipe)
        {
            if (byRecipe.Name != "pieslice")
            {
                base.OnCreatedByCrafting(allInputslots, outputSlot, byRecipe);
                return;
            }

            if (outputSlot.Itemstack == null) return;

            foreach (ItemSlot slot in allInputslots)
            {
                if (slot.Itemstack?.Collectible is not BlockPie) continue;

                ItemStack pieStack = slot.Itemstack.Clone();
                outputSlot.Itemstack = TakeSlice(ref pieStack!);
            }
        }

        public override void OnConsumedByCrafting(ItemSlot[] allInputSlots, ItemSlot stackInSlot, IRecipeBase recipe, IRecipeIngredient fromIngredient, IPlayer byPlayer, int quantity)
        {
            if (recipe.Name != "pieslice")
            {
                base.OnConsumedByCrafting(allInputSlots, stackInSlot, recipe, fromIngredient, byPlayer, quantity);
                return;
            }

            ItemStack? pieStack = stackInSlot.Itemstack?.Clone();
            TakeSlice(ref pieStack);
            if (pieStack?.Attributes.GetAsInt("pieSize") == 1)
            {
                pieStack.Attributes.SetFloat("quantityServings", 0.25f);
                pieStack.Attributes.SetBool("bakeable", false);
            }
            stackInSlot.Itemstack = pieStack;
        }

        public override int GetRandomContentColor(ICoreClientAPI capi, ItemStack[] stacks)
        {
            if (stacks.Length == 0) return 0x808080;
            ItemStack[] cstacks = GetContents(capi.World, stacks[0]);
            if (cstacks.Length == 0) return 0x808080;

            ItemStack rndStack = cstacks[capi.World.Rand.Next(stacks.Length)];
            return rndStack.Collectible.GetRandomColor(capi, rndStack);
        }


        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            WorldInteraction[] baseinteractions = [ .. base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)
                .Where(bi => bi.ActionLangCode != "blockhelp-meal-eat" && bi.ActionLangCode != "blockhelp-behavior-rightclickpickup")];

            WorldInteraction[] allinteractions = interactions.Append(baseinteractions);
            return allinteractions;
        }

        public static List<CookingRecipe> GetHandbookRecipes(ICoreAPI api, ItemStack[] allStacks)
        {
            List<ItemStack> crusts = [];
            List<ItemStack> noMixFillings = [];
            Dictionary<string, List<ItemStack>> mixedFillings = [];
            List<ItemStack> toppings = [];

            foreach (ItemStack stack in allStacks)
            {
                if (InPieProperties.ReadFrom(stack) is not InPieProperties pieProps) continue;

                switch (pieProps.PartType)
                {
                    case EnumPiePartType.Crust:
                        crusts.Add(stack);
                        toppings.Add(stack);
                        break;
                    case EnumPiePartType.Filling:
                        if (pieProps.AllowMixing)
                        {
                            foreach (string mixCode in pieProps.MixingCodes)
                            {
                                if (mixedFillings.TryGetValue(mixCode, out List<ItemStack>? value)) value.Add(stack);
                                else mixedFillings.Add(mixCode, [stack]);
                            }
                        }
                        else
                        {
                            noMixFillings.Add(stack);
                        }
                        break;
                    case EnumPiePartType.Topping:
                        toppings.Add(stack);
                        break;
                }
            }

            return
            [
                .. mixedFillings.Select(entry => CreateRecipe(api.World, "mixed-" + entry.Key.ToLowerInvariant(), crusts, [.. entry.Value], toppings)),
                .. noMixFillings.Select(stack => CreateRecipe(api.World, "single-" + stack.Collectible.Code.ToShortString(), crusts, [stack], toppings))
            ];
        }

        private static CookingRecipe CreateRecipe(IWorldAccessor world, string code, List<ItemStack> crusts, List<ItemStack> fillings, List<ItemStack> toppings, bool mixedRecipe = false)
        {
            return new()
            {
                Code = code,
                Ingredients =
                [
                    new ()
                    {
                        Code = "dough",
                        TypeName = "bottomcrust",
                        MinQuantity = 1,
                        MaxQuantity = 1,
                        ValidStacks = [.. crusts.Select<ItemStack, CookingRecipeStack>(crust => new ()
                        {
                            Code = crust.Collectible.Code,
                            Type = crust.Collectible.ItemClass,
                            Quantity = 2,
                            ResolvedItemstack = crust.Clone()
                        })]
                    },
                    new ()
                    {
                        Code = "filling",
                        TypeName = "piefilling",
                        MinQuantity = 4,
                        MaxQuantity = 4,
                        ValidStacks = [.. fillings.Select<ItemStack, CookingRecipeStack>(filling => new ()
                        {
                            Code = filling.Collectible.Code,
                            Type = filling.Collectible.ItemClass,
                            Quantity = 2,
                            ResolvedItemstack = filling.Clone()
                        })]
                    },
                    new ()
                    {
                        Code = "crust",
                        TypeName = "topcrust",
                        MinQuantity = 0,
                        MaxQuantity = 1,
                        ValidStacks = [.. toppings.Select<ItemStack, CookingRecipeStack>(topping => new ()
                        {
                            Code = topping.Collectible.Code,
                            Type = topping.Collectible.ItemClass,
                            Quantity = 2,
                            ResolvedItemstack = topping.Clone()
                        })]
                    }
                ],
                PerishableProps = new()
            };
        }

        public static ItemStack?[] GenerateRandomPie(ICoreAPI api, ref Dictionary<CookingRecipeIngredient, HashSet<ItemStack?>>? cachedValidStacksByIngredient, CookingRecipe recipe, ItemStack? ingredientStack = null)
        {
            if (recipe.Ingredients == null) return new ItemStack?[6];

            Dictionary<CookingRecipeIngredient, HashSet<ItemStack?>>? validStacksByIngredient = cachedValidStacksByIngredient;

            if (validStacksByIngredient == null)
            {
                validStacksByIngredient = [];

                foreach (CookingRecipeIngredient? ingredient in recipe.Ingredients)
                {
                    HashSet<ItemStack?> ingredientStacks = [];

                    ingredient.Resolve(api.World, "handbook meal recipes");
                    foreach (ItemStack? astack in ingredient.ValidStacks.Select(stack => stack.ResolvedItemstack))
                    {
                        if (ingredient.GetMatchingStack(astack) is not { } vstack) continue;

                        if (astack?.Clone() is { } stack && BlockLiquidContainerBase.GetContainableProps(stack) is { } props)
                        {
                            stack.StackSize = vstack.StackSize * (int)(props.ItemsPerLitre * ingredient.PortionSizeLitres);
                            ingredientStacks.Add(stack);
                        }
                        else
                        {
                            ingredientStacks.Add(null);
                        }
                    }

                    if (ingredient.MinQuantity <= 0) ingredientStacks.Add(null);

                    validStacksByIngredient.Add(ingredient.Clone(), ingredientStacks);
                }

                cachedValidStacksByIngredient = validStacksByIngredient;
            };

            if (validStacksByIngredient == null) return new ItemStack?[6];



            void addIngredient(ref List<ItemStack?> pie, string code, ref Dictionary<CookingRecipeIngredient, List<ItemStack?>> valIngStacks, ref CookingRecipeIngredient? requestedIngredient)
            {
                (CookingRecipeIngredient ingredient, List<ItemStack?> validStacks) = valIngStacks.FirstOrDefault(entry => entry.Key.Code == code);

                // Try to fulfill the ingredient request
                if (ingredient.Code == requestedIngredient?.Code)
                {
                    if (validStacks.First(stack => stack?.Collectible.Code == ingredientStack?.Collectible.Code) is ItemStack stack)
                    {
                        pie.Add(stack.Clone());

                        ingredient.MinQuantity--;
                        ingredient.MaxQuantity--;
                    }

                    requestedIngredient = null;
                }

                // Only fillings need the code below here for filtering, so we skip the
                // list copying if possible.
                if (code != "filling")
                {
                    pie.Add(validStacks[api.World.Rand.Next(validStacks.Count)]?.Clone());
                    return;
                }

                // When we add an ingredient, we filter out all the other ingredients
                // that have any of the same codes that aren't the recipe code.
                // This ensures we get the widest selection possible
                // in order to minimize the chance of accidentally getting a pie
                // that is considered a different category than requested.
                //
                // We try to include as many codes as possible to avoid accidentally
                // getting a more specific type of pie, e.g. we don't want to get all
                // vegetables when we're trying to generate a pot pie.

                List<ItemStack?> filteredValidStacks = validStacks;
                string recipeCode = recipe.Code?.Split("-").ElementAtOrDefault(1) ?? "";
                while (ingredient.MinQuantity > 0)
                {
                    if (filteredValidStacks.Count > 0)
                    {
                        ItemStack? stack = filteredValidStacks[api.World.Rand.Next(filteredValidStacks.Count)]?.Clone();
                        // Get the list of codes for this ingredient that can be filtered out
                        string[] ingredientCodes = InPieProperties.ReadFrom(stack)!.MixingCodes.Where(code => code != recipeCode)?.ToArray() ?? [];
                        // Remove all the other ingredients that share any codes
                        filteredValidStacks = filteredValidStacks.Where(stack => InPieProperties.ReadFrom(stack)!.MixingCodes.Intersect(ingredientCodes).Count() == 0).ToList();
                        pie.Add(stack);
                    }
                    else
                    {
                        pie.Add(validStacks[api.World.Rand.Next(validStacks.Count)]?.Clone());
                    }

                    ingredient.MinQuantity--;
                    ingredient.MaxQuantity--;
                }
            }



            List<ItemStack?> randomPie = [];
            while (!recipe.Matches([.. randomPie]))
            {
                Dictionary<CookingRecipeIngredient, List<ItemStack?>> valIngStacks = [];
                foreach (var entry in validStacksByIngredient) valIngStacks.Add(entry.Key.Clone(), [.. entry.Value]);
                valIngStacks = valIngStacks.OrderBy(x => api.World.Rand.Next()).ToDictionary(item => item.Key, item => item.Value);

                CookingRecipeIngredient? requestedIngredient = null;
                if (ingredientStack != null)
                {
                    List<CookingRecipeIngredient> validIngredients = [.. recipe.Ingredients.Where(ingredient => ingredient.Matches(ingredientStack))];
                    requestedIngredient = validIngredients[api.World.Rand.Next(validIngredients.Count)].Clone();
                }

                randomPie = [];
                addIngredient(ref randomPie, "dough", ref valIngStacks, ref requestedIngredient);
                addIngredient(ref randomPie, "filling", ref valIngStacks, ref requestedIngredient);
                addIngredient(ref randomPie, "crust", ref valIngStacks, ref requestedIngredient);

                while (randomPie.Count < 6) randomPie.Add(null);
            }
            return [.. randomPie];
        }

        [return: NotNullIfNotNull(nameof(pieStack))]
        public static ItemStack? CycleTopCrustType(ItemStack? pieStack)
        {
            if (pieStack == null) return null;

            string topCrustType = GetTopCrustType(pieStack);

            pieStack.Attributes.SetString("topCrustType", TopCrustTypes[(TopCrustTypes.IndexOf(type => type.Code.EqualsFast(topCrustType)) + 1) % TopCrustTypes.Length].Code);
            return pieStack;
        }

        [return: NotNullIfNotNull(nameof(pieStack))]
        public static string? GetTopCrustType(ItemStack? pieStack)
        {
            if (pieStack == null) return null;

            string topCrustType = pieStack.Attributes.GetAsString("topCrustType", "full");
            if (!TopCrustTypes.Any(type => type.Code.EqualsFast(topCrustType)))
            {
                switch (topCrustType.ToInt())
                {
                    default:
                    case 0:
                        topCrustType = "full";
                        break;
                    case 1:
                        topCrustType = "square";
                        break;
                    case 2:
                        topCrustType = "diagonal";
                        break;
                }

                pieStack.Attributes.SetString("topCrustType", topCrustType);
            }

            return topCrustType;
        }

        public override string HandbookPageCodeForStack(IWorldAccessor world, ItemStack stack)
        {
            string? type = null;

            if (GetContents(world, stack) is ItemStack[] contents && contents.Length > 1)
            {
                if (InPieProperties.ReadFrom(contents[1])?.AllowMixing == false)
                {
                    type = "single-" + contents[1].Collectible.Code.ToShortString();
                }
                else type = "mixed-" + IngredientFoodCategory(contents[1]).ToString().ToLowerInvariant();

                return "handbook-mealrecipe-" + type + "-pie";
            }
            else return "craftinginfo-pie";
        }
    }
}

