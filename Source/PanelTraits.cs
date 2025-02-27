using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;
namespace EdB.PrepareCarefully {
    public class PanelTraits : PanelBase {
        public delegate void RandomizeTraitsHandler();
        public delegate void AddTraitHandler(Trait trait);
        public delegate void UpdateTraitHandler(int index, Trait trait);
        public delegate void RemoveTraitHandler(Trait trait);

        public event RandomizeTraitsHandler TraitsRandomized;
        public event AddTraitHandler TraitAdded;
        public event UpdateTraitHandler TraitUpdated;
        public event RemoveTraitHandler TraitRemoved;

        private ProviderTraits providerTraits = new ProviderTraits();
        protected ScrollViewVertical scrollView = new ScrollViewVertical();
        protected List<Field> fields = new List<Field>();
        protected List<Trait> traitsToRemove = new List<Trait>();
        protected HashSet<TraitDef> disallowedTraitDefs = new HashSet<TraitDef>();
        protected Dictionary<Trait,string> conflictingTraitList = new Dictionary<Trait, string>();

        protected Vector2 SizeField;
        protected Vector2 SizeTrait;
        protected Vector2 SizeFieldPadding = new Vector2(5, 6);
        protected Vector2 SizeTraitMargin = new Vector2(4, -6);
        protected Rect RectScrollFrame;
        protected Rect RectScrollView;

        public class TipCache {
            public Dictionary<Trait, string> Lookup = new Dictionary<Trait, string>();
            private CustomPawn pawn = null;
            private bool ready = false;
            public void CheckPawn(CustomPawn pawn) {
                if (this.pawn != pawn) {
                    this.pawn = pawn;
                    Invalidate();
                }
            }
            public void Invalidate() {
                this.ready = false;
                Lookup.Clear();
            }
            public void MakeReady() {
                this.ready = true;
            }
            public bool Ready {
                get {
                    return ready;
                }
            }
        }
        protected TipCache tipCache = new TipCache();

        public PanelTraits() {
        }

        public override string PanelHeader {
            get {
                return "Traits".Translate();
            }
        }

        public override void Resize(Rect rect) {
            base.Resize(rect);

            float panelPadding = 10;
            float fieldHeight = Style.FieldHeight;
            SizeTrait = new Vector2(PanelRect.width - panelPadding * 2, fieldHeight + SizeFieldPadding.y * 2);
            SizeField = new Vector2(SizeTrait.x - SizeFieldPadding.x * 2, SizeTrait.y - SizeFieldPadding.y * 2);

            RectScrollFrame = new Rect(panelPadding, BodyRect.y,
                PanelRect.width - panelPadding * 2, BodyRect.height - panelPadding);
            RectScrollView = new Rect(0, 0, RectScrollFrame.width, RectScrollFrame.height);
        }

        protected override void DrawPanelContent(State state) {
            CustomPawn currentPawn = state.CurrentPawn;
            tipCache.CheckPawn(currentPawn);

            base.DrawPanelContent(state);

            Action clickAction = null;
            float cursor = 0;
            GUI.color = Color.white;
            GUI.BeginGroup(RectScrollFrame);
            try {
                if (currentPawn.Traits.Count() == 0) {
                    GUI.color = Style.ColorText;
                    Widgets.Label(RectScrollView.InsetBy(6, 0, 0, 0), "EdB.PC.Panel.Traits.None".Translate());
                }
                GUI.color = Color.white;

                scrollView.Begin(RectScrollView);

                int index = 0;
                foreach (Trait trait in currentPawn.Traits) {
                    if (index >= fields.Count) {
                        fields.Add(new Field());
                    }
                    Field field = fields[index];

                    GUI.color = Style.ColorPanelBackgroundItem;
                    Rect traitRect = new Rect(0, cursor, SizeTrait.x - (scrollView.ScrollbarsVisible ? 16 : 0), SizeTrait.y);
                    GUI.DrawTexture(traitRect, BaseContent.WhiteTex);
                    GUI.color = Color.white;

                    Rect fieldRect = new Rect(SizeFieldPadding.x, cursor + SizeFieldPadding.y, SizeField.x, SizeField.y);
                    if (scrollView.ScrollbarsVisible) {
                        fieldRect.width = fieldRect.width - 16;
                    }
                    field.Rect = fieldRect;
                    Rect fieldClickRect = fieldRect;
                    fieldClickRect.width = fieldClickRect.width - 36;
                    field.ClickRect = fieldClickRect;

                    if (trait != null) {
                        field.Label = trait.LabelCap;
                        field.Tip = GetTraitTip(trait, currentPawn);
                    }
                    else {
                        field.Label = null;
                        field.Tip = null;
                    }
                    Trait localTrait = trait;
                    int localIndex = index;
                    field.ClickAction = () => {
                        Trait originalTrait = localTrait;
                        Trait selectedTrait = originalTrait;
                        ComputeDisallowedTraits(currentPawn, originalTrait);
                        Dialog_Options<Trait> dialog = new Dialog_Options<Trait>(providerTraits.Traits) {
                            NameFunc = (Trait t) => {
                                return t.LabelCap;
                            },
                            DescriptionFunc = (Trait t) => {
                                return GetTraitTip(t, currentPawn);
                            },
                            SelectedFunc = (Trait t) => {
                                if ((selectedTrait == null || t == null) && selectedTrait != t) {
                                    return false;
                                }
                                return selectedTrait.def == t.def && selectedTrait.Label == t.Label;
                            },
                            SelectAction = (Trait t) => {
                                selectedTrait = t;
                            },
                            EnabledFunc = (Trait t) => {
                                return !disallowedTraitDefs.Contains(t.def);
                            },
                            CloseAction = () => {
                                TraitUpdated(localIndex, selectedTrait);
                            },
                            NoneSelectedFunc = () => {
                                return selectedTrait == null;
                            },
                            SelectNoneAction = () => {
                                selectedTrait = null;
                            }
                        };
                        Find.WindowStack.Add(dialog);
                    };
                    field.PreviousAction = () => {
                        var capturedIndex = index;
                        clickAction = () => {
                            SelectPreviousTrait(currentPawn, capturedIndex);
                        };
                    };
                    field.NextAction = () => {
                        var capturedIndex = index;
                        clickAction = () => {
                            SelectNextTrait(currentPawn, capturedIndex);
                        };
                    };
                    field.Draw();

                    // Remove trait button.
                    Rect deleteRect = new Rect(field.Rect.xMax - 32, field.Rect.y + field.Rect.HalfHeight() - 6, 12, 12);
                    if (deleteRect.Contains(Event.current.mousePosition)) {
                        GUI.color = Style.ColorButtonHighlight;
                    }
                    else {
                        GUI.color = Style.ColorButton;
                    }
                    GUI.DrawTexture(deleteRect, Textures.TextureButtonDelete);
                    if (Widgets.ButtonInvisible(deleteRect, false)) {
                        SoundDefOf.Tick_Tiny.PlayOneShotOnCamera();
                        traitsToRemove.Add(trait);
                    }

                    index++;

                    cursor += SizeTrait.y + SizeTraitMargin.y;
                }
                cursor -= SizeTraitMargin.y;
            }
            finally {
                scrollView.End(cursor);
                GUI.EndGroup();
            }

            tipCache.MakeReady();

            GUI.color = Color.white;

            if (clickAction != null) {
                clickAction();
                clickAction = null;
            }

            // Randomize traits button.
            Rect randomizeRect = new Rect(PanelRect.width - 32, 9, 22, 22);
            if (randomizeRect.Contains(Event.current.mousePosition)) {
                GUI.color = Style.ColorButtonHighlight;
            }
            else {
                GUI.color = Style.ColorButton;
            }
            GUI.DrawTexture(randomizeRect, Textures.TextureButtonRandom);
            if (Widgets.ButtonInvisible(randomizeRect, false)) {
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                tipCache.Invalidate();
                TraitsRandomized();
            }

            // Add trait button.
            Rect addRect = new Rect(randomizeRect.x - 24, 12, 16, 16);
            Style.SetGUIColorForButton(addRect);
            int traitCount = state.CurrentPawn.Traits.Count();
            bool addButtonEnabled = (state.CurrentPawn != null && traitCount < Constraints.MaxTraits);
            if (!addButtonEnabled) {
                GUI.color = Style.ColorButtonDisabled;
            }
            GUI.DrawTexture(addRect, Textures.TextureButtonAdd);
            if (addButtonEnabled && Widgets.ButtonInvisible(addRect, false)) {
                ComputeDisallowedTraits(currentPawn, null);
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
                Trait selectedTrait = null;
                Dialog_Options<Trait> dialog = new Dialog_Options<Trait>(providerTraits.Traits) {
                    ConfirmButtonLabel = "EdB.PC.Common.Add".Translate(),
                    NameFunc = (Trait t) => {
                        return t.LabelCap;
                    },
                    DescriptionFunc = (Trait t) => {
                        return GetTraitTip(t, state.CurrentPawn);
                    },
                    SelectedFunc = (Trait t) => {
                        return selectedTrait == t;
                    },
                    SelectAction = (Trait t) => {
                        selectedTrait = t;
                    },
                    EnabledFunc = (Trait t) => {
                        return !disallowedTraitDefs.Contains(t.def);
                    },
                    CloseAction = () => {
                        if (selectedTrait != null) {
                            TraitAdded(selectedTrait);
                            tipCache.Invalidate();
                        }
                    }
                };
                Find.WindowStack.Add(dialog);
            }

            if (traitsToRemove.Count > 0) {
                foreach (var trait in traitsToRemove) {
                    TraitRemoved(trait);
                }
                traitsToRemove.Clear();
                tipCache.Invalidate();
            }
        }

        protected string GetTraitTip(Trait trait, CustomPawn pawn) {
            if (!tipCache.Ready || !tipCache.Lookup.ContainsKey(trait)) {
                string value = GenerateTraitTip(trait, pawn);
                tipCache.Lookup.Add(trait, value);
                return value;
            }
            else {
                return tipCache.Lookup[trait];
            }
        }

        protected string GenerateTraitTip(Trait trait, CustomPawn pawn) {
            try {
                string baseTip = trait.TipString(pawn.Pawn);
                string conflictingNames = null;
                if (!conflictingTraitList.TryGetValue(trait, out conflictingNames)) {
                    List<Trait> conflictingTraits = providerTraits.Traits.Where((Trait t) => {
                        return trait.def.conflictingTraits.Contains(t.def) || (t.def == trait.def && t.Label != trait.Label);
                    }).ToList();
                    if (conflictingTraits.Count == 0) {
                        conflictingTraitList.Add(trait, null);
                    }
                    else {
                        conflictingNames = "";
                        if (conflictingTraits.Count == 1) {
                            conflictingNames = "EdB.PC.Panel.Traits.Tip.Conflict.List.1".Translate(conflictingTraits[0].LabelCap);
                        }
                        else if (conflictingTraits.Count == 2) {
                            conflictingNames = "EdB.PC.Panel.Traits.Tip.Conflict.List.2".Translate(conflictingTraits[0].LabelCap, conflictingTraits[1].LabelCap);
                        }
                        else {
                            int c = conflictingTraits.Count;
                            conflictingNames = "EdB.PC.Panel.Traits.Tip.Conflict.List.Last".Translate(conflictingTraits[c - 2].LabelCap, conflictingTraits[c - 1].LabelCap);
                            for (int i = c - 3; i >= 0; i--) {
                                conflictingNames = "EdB.PC.Panel.Traits.Tip.Conflict.List.Many".Translate(conflictingTraits[i].LabelCap, conflictingNames);
                            }
                        }
                        conflictingTraitList.Add(trait, conflictingNames);
                    }
                }
                if (conflictingNames == null) {
                    return baseTip;
                }
                else {
                    return "EdB.PC.Panel.Traits.Tip.Conflict".Translate(baseTip, conflictingNames).Resolve();
                }
            }
            catch (Exception e) {
                Logger.Warning("There was an error when trying to generate a mouseover tip for trait {" + (trait?.LabelCap ?? "null") + "}\n" + e);
                return null;
            }
        }

        protected void ComputeDisallowedTraits(CustomPawn customPawn, Trait traitToReplace) {
            disallowedTraitDefs.Clear();
            foreach (Trait t in customPawn.Traits) {
                if (t == traitToReplace) {
                    continue;
                }
                disallowedTraitDefs.Add(t.def);
                if (t.def.conflictingTraits != null) {
                    foreach (var c in t.def.conflictingTraits) {
                        disallowedTraitDefs.Add(c);
                    }
                }
            }
        }

        protected void SelectNextTrait(CustomPawn customPawn, int traitIndex) {
            Trait currentTrait = customPawn.GetTrait(traitIndex);
            ComputeDisallowedTraits(customPawn, currentTrait);
            int index = -1;
            if (currentTrait != null) {
                index = providerTraits.Traits.FindIndex((Trait t) => {
                    return t.Label.Equals(currentTrait.Label);
                });
            }
            int count = 0;
            do {
                index++;
                if (index >= providerTraits.Traits.Count) {
                    index = 0;
                }
                if (++count > providerTraits.Traits.Count + 1) {
                    index = -1;
                    break;
                }
            }
            while (index != -1 && (customPawn.HasTrait(providerTraits.Traits[index]) || disallowedTraitDefs.Contains(providerTraits.Traits[index].def)));

            Trait newTrait = null;
            if (index > -1) {
                newTrait = providerTraits.Traits[index];
            }
            TraitUpdated(traitIndex, newTrait);
        }

        protected void SelectPreviousTrait(CustomPawn customPawn, int traitIndex) {
            Trait currentTrait = customPawn.GetTrait(traitIndex);
            ComputeDisallowedTraits(customPawn, currentTrait);
            int index = -1;
            if (currentTrait != null) {
                index = providerTraits.Traits.FindIndex((Trait t) => {
                    return t.Label.Equals(currentTrait.Label);
                });
            }
            int count = 0;
            do {
                index--;
                if (index < 0) {
                    index = providerTraits.Traits.Count - 1;
                }
                if (++count > providerTraits.Traits.Count + 1) {
                    index = -1;
                    break;
                }
            }
            while (index != -1 && (customPawn.HasTrait(providerTraits.Traits[index]) || disallowedTraitDefs.Contains(providerTraits.Traits[index].def)));

            Trait newTrait = null;
            if (index > -1) {
                newTrait = providerTraits.Traits[index];
            }
            TraitUpdated(traitIndex, newTrait);
        }

        protected void ClearTrait(CustomPawn customPawn, int traitIndex) {
            TraitUpdated(traitIndex, null);
            tipCache.Invalidate();
        }

        public void ScrollToTop() {
            scrollView.ScrollToTop();
        }

        public void ScrollToBottom() {
            scrollView.ScrollToBottom();
        }
    }
}
