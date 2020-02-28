﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace CombatExtended
{
    public class CompAmmoUser : CompRangedGizmoGiver
    {

        #region Fields
        private int curMagCountInt = 0;

        private Thing ammoToBeDeleted;

        public List<Thing> adders = new List<Thing>();
        public List<Thing> spentAdders = new List<Thing>();

        public Thing CurrentAdder => adders.FirstOrDefault();
        public int currentAdderCharge = 0;
        
        public Building_TurretGunCE turret;         // Cross-linked from CE turret

        internal static Type rgStance = null;       // RunAndGun compatibility, set in relevent patch if needed
        #endregion

        #region Properties

        public CompProperties_AmmoUser Props
        {
            get
            {
                return (CompProperties_AmmoUser)props;
            }
        }

        /// <summary>Cached whether gun ejects cases for faster SpentRounds calculation</summary>
        bool ejectsCasings = false;
        public bool DiscardRounds => ejectsCasings
            && ((MainProjectile.projectile as ProjectilePropertiesCE)?.dropsCasings ?? false);
        
        public int CurMagCount
        {
            get
            {
                return curMagCountInt;
            }
            set
            {
                if (curMagCountInt != value && value >= 0)
                {
                    curMagCountInt = value;

                    if (CompInventory != null) CompInventory.UpdateInventory();     //Must be positioned after curMagCountInt is updated, because it relies on that value
                }
            }
        }
        public CompEquippable CompEquippable
        {
            get { return parent.GetComp<CompEquippable>(); }
        }
        public Pawn Wielder
        {
            get
            {
                if (CompEquippable == null 
                    || CompEquippable.PrimaryVerb == null 
                    || CompEquippable.PrimaryVerb.caster == null
                    || ((CompEquippable?.parent?.ParentHolder as Pawn_InventoryTracker)?.pawn is Pawn holderPawn && holderPawn != CompEquippable?.PrimaryVerb?.CasterPawn))
                {
                    return null;
                }
                return CompEquippable.PrimaryVerb.CasterPawn;
            }
        }
        public Pawn Holder
        {
            get
            {
                return Wielder ?? (CompEquippable.parent.ParentHolder as Pawn_InventoryTracker)?.pawn;
            }
        }
        public bool UseAmmo
        {
            get
            {
                return Controller.settings.EnableAmmoSystem && Props.ammoSet != null;
            }
        }
        public bool HasAndUsesAmmoOrMagazine
        {
            get
            {
                return !UseAmmo || HasAmmoOrMagazine;
            }
        }
        public bool HasAmmoOrMagazine
        {
            get
            {
                return (HasMagazine && CurMagCount > 0) || HasAmmo;
            }
        }
        public bool CanBeFiredNow
        {
            get
            {
                return (HasMagazine && CurMagCount > 0) || (!HasMagazine && (HasAmmo || !UseAmmo));
            }
        }
        //TODO: Split into HasAmmo(for current link) and HasAmmo(for any link)
        public bool HasAmmo => CompInventory?.ammoList.Any(x => Props.ammoSet.MaxCharge(x.def) > 0) ?? false;
        public bool HasMagazine { get { return Props.magazineSize > 0; } }
      /*public AmmoDef CurrentAmmo
        {
            get
            {
                return UseAmmo ? currentAmmoInt : null;
            }
        }*/

        int currentLinkInt;
        public AmmoLink CurrentLink => Props.ammoSet.ammoTypes[currentLinkInt];
        int selectedLinkInt;
        public AmmoLink SelectedLink
        {
            get
            {
                return Props.ammoSet.ammoTypes[selectedLinkInt];
            }
            set
            {
                selectedLinkInt = Props.ammoSet.ammoTypes.IndexOf(value);

                if (!HasMagazine && !LinksMatch)
                    currentLinkInt = selectedLinkInt;
            }
        }
        public bool LinksMatch => selectedLinkInt == currentLinkInt;

        public ChargeUser CurrentUser => CurrentLink.BestUser(this);
        public ChargeUser latestUser;
        public ThingDef MainProjectile => CurrentUser?.projectiles.First().thingDef ?? null;

      //public AmmoLink CurrentLink => Props.ammoSet?.ammoTypes?
      //            .Where(x => x.adders.Any(y => y.ammo.thingDef == CurrentAmmo) && x.amount <= CurMagCount)
      //            .MaxByWithFallback(x => x.amount);

        //Shouldn't exist
      //public ThingDef CurAmmoProjectile => CurrentLink?.projectile
      //        ?? parent.def.Verbs.FirstOrDefault().defaultProjectile;
        public CompInventory CompInventory
        {
            get
            {
                return Holder.TryGetComp<CompInventory>();
            }
        }
        private IntVec3 Position
        {
            get
            {
                if (Wielder != null) return Wielder.Position;
                else if (turret != null) return turret.Position;
                else if (Holder != null) return Holder.Position;
                else return parent.Position;
            }
        }
        private Map Map
        {
            get
            {
                if (Holder != null) return Holder.MapHeld;
                else if (turret != null) return turret.MapHeld;
                else return parent.MapHeld;
            }
        }
        public bool ShouldThrowMote => Props.throwMote && Props.magazineSize > 1;

        /* Shouldn't exist
        public AmmoDef SelectedAmmo
        {
            get
            {
                return selectedAmmo;
            }
            set
            {
                selectedAmmo = value;
                if (!HasMagazine && CurrentAmmo != value)
                {
                    currentAmmoInt = value;
                }
            }
        }
        */

        #endregion Properties
        
        #region Methods
        
        public override void Initialize(CompProperties vprops)
        {
            base.Initialize(vprops);

            //curMagCountInt = Props.spawnUnloaded && UseAmmo ? 0 : Props.magazineSize;
            ejectsCasings = parent.def.Verbs.Select(x => x as VerbPropertiesCE).First()?.ejectsCasings ?? true;

            // Initialize ammo with default if none is set
            if (UseAmmo)
            {
                if (Props.ammoSet.ammoTypes.NullOrEmpty())
                {
                    Log.Error(parent.Label + " has no available ammo types");
                }
                else
                {
                    currentLinkInt = 0;
                    selectedLinkInt = 0;

                  /*if (currentAmmoInt == null)
                        currentAmmoInt = (AmmoDef)Props.ammoSet.ammoTypes[0].adders.MinBy(x => x.count).thingDef;
                    if (selectedAmmo == null)
                        selectedAmmo = currentAmmoInt;*/
                }
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref curMagCountInt, "count", 0);
            Scribe_Values.Look(ref currentAdderCharge, "deficit", 0);

            Scribe_Values.Look<int>(ref currentLinkInt, "currentLinkInt", 0);
            Scribe_Values.Look<int>(ref selectedLinkInt, "selectedLinkInt", currentLinkInt);
            if (currentLinkInt > Props.ammoSet.ammoTypes.Count)     currentLinkInt = 0;
            if (selectedLinkInt > Props.ammoSet.ammoTypes.Count)    selectedLinkInt = 0;
            
            Scribe_Collections.Look<Thing>(ref adders, "adders");
            Scribe_Collections.Look<Thing>(ref spentAdders, "spentAdders");
            
            ejectsCasings = parent.def.Verbs.Select(x => x as VerbPropertiesCE).First()?.ejectsCasings ?? true;
        }

        private void AssignJobToWielder(Job job)
        {
            if (Wielder.drafter != null)
            {
                Wielder.jobs.TryTakeOrderedJob(job);
            }
            else
            {
                ExternalPawnDrafter.TakeOrderedJob(Wielder, job);
            }
        }

        #region Firing
        #region Bows
        //Of relevance to ammousers without mag (bows)
        public bool Notify_ShotFired()
        {
            if (ammoToBeDeleted != null)
            {
                ammoToBeDeleted.Destroy();
                ammoToBeDeleted = null;
                CompInventory.UpdateInventory();
                if (!HasAmmoOrMagazine)
                {
                    return false;
                }
            }
            return true;
        }

        //Of relevance to ammousers without mag (bows)
        public bool Notify_PostShotFired()
        {
            if (!HasAmmoOrMagazine)
            {
                DoOutOfAmmoAction();
                return false;
            }
            return true;
        }
        #endregion

        /// <summary>
        /// Reduces ammo count and updates inventory if necessary, call this whenever ammo is consumed by the gun (e.g. firing a shot, clearing a jam).
        /// </summary>
        public bool TryFire()
        {
            Log.Message("TryFire"
                + " Adder: " + string.Join(",", adders.Select((x, y) => "[" + y + "]" + x.def.defName + "=" + x.stackCount).ToArray())
                + " Spent: " + string.Join(",", spentAdders.Select((x, y) => "[" + y + "]" + x.def.defName + "=" + x.stackCount).ToArray())
                + " cMC: " + curMagCountInt
                + " cAC: " + currentAdderCharge);

            if (Wielder == null && turret == null)
                Log.Error(parent.ToString() + " tried reducing its ammo count without a wielder");
            
            var chargesUsed = CurrentUser?.chargesUsed ?? 1;

            // If magazine is empty, return false
            if (CurMagCount <= 0)
            {
                CurMagCount = 0;
                return false;
            }

            // Reduce ammo count
            curMagCountInt -= chargesUsed;

            // Mag-less weapons feed directly from inventory
            if (!HasMagazine && CurMagCount <= 0)
                return LoadAmmo();

            if (HasMagazine)
            {
                //Decrease currentAdder's charges
                currentAdderCharge -= chargesUsed;

                if (!TryFeed())
                {
                    TryStartReload();
                    return true;
                }
            }

            //Update inventory
            CurMagCount = curMagCountInt;

            if (curMagCountInt < 0) TryStartReload();
            return true;
        }

        public bool TryFeed()
        {
            //Replenish currentAdder's charges with next adder
            while (currentAdderCharge < 0)
            {
                //Find new currentAdder
                if (CurrentLink.CanAdd(CurrentAdder.def, out var newChargeCount))
                {
                    //Has issue
                    var amountDepleted = CurrentLink.AmountForDeficit(CurrentAdder, -currentAdderCharge, true);

                    //Add count from currentAdder
                    currentAdderCharge += amountDepleted * newChargeCount;

                    //Runs perfectly
                    if (amountDepleted > 0)
                        UnloadAdder(CurrentAdder.SplitOff(amountDepleted), true);
                    else
                        return false;
                }
                //No new currentAdder found.. time for reloading
                else
                {
                    return false;
                }
            }

            return currentAdderCharge >= 0;
        }
        #endregion

        #region Reloading
        public void AddAdder(Thing inThing, bool toSpentAdders = false, bool updateInventory = true)
        {
            if (inThing == null)
                return;

            Thing thing = null;
            if (!toSpentAdders)
            {
                //Add appropriate number of charges (and check for all limitations etc. set out by the ammoSetDef)
                curMagCountInt += SelectedLink.LoadThing(inThing, this, out var count);
                thing = inThing.SplitOff(count);
            }
            else
                thing = inThing;

            if (thing != null)
            {
                var existingAdder = (toSpentAdders ? spentAdders : adders).Find(x => x.def == thing.def);

                if (existingAdder == null || !existingAdder.TryAbsorbStack(thing, true))
                    (toSpentAdders ? spentAdders : adders).Add(thing);
            }

            if (updateInventory)
                CurMagCount = curMagCountInt;

            //TODO: Handle inThing? Destroy it?
        }

        // really only used by pawns (JobDriver_Reload) at this point... TODO: Finish making sure this is only used by pawns and fix up the error checking.
        /// <summary>
        /// Overrides a Pawn's current activities to start reloading a gun or turret.  Has a code path to resume the interrupted job.
        /// </summary>
        public void TryStartReload()
        {
            Log.Message("TryStartReload-Pre"
                + " Adder: " + string.Join(",", adders.Select((x, y) => "[" + y + "]" + x.def.defName + "=" + x.stackCount).ToArray())
                + " Spent: " + string.Join(",", spentAdders.Select((x, y) => "[" + y + "]" + x.def.defName + "=" + x.stackCount).ToArray())
                + " cMC: " + curMagCountInt
                + " cAC: " + currentAdderCharge);
            #region Checks
            if (!HasMagazine)
            {
                if (!CanBeFiredNow)
                {
                    DoOutOfAmmoAction();
                }
                return;
            }
            if (Wielder == null && turret == null)
                return;

            // secondary branch for if we ended up being called up by a turret somehow...
            if (turret != null)
            {
                turret.TryOrderReload();
                return;
            }

            // R&G compatibility, prevents an initial attempt to reload while moving
            if (Wielder.stances.curStance.GetType() == rgStance)
                return;
            #endregion

            if (UseAmmo)
            {
                TryUnload();

                // Check for ammo
                if (Wielder != null && !HasAmmo)
                {
                    DoOutOfAmmoAction();
                    return;
                }
            }

            Log.Message("TryStartReload-Post"
                + " Adder: " + string.Join(",", adders.Select((x, y) => "[" + y + "]" + x.def.defName + "=" + x.stackCount).ToArray())
                + " Spent: " + string.Join(",", spentAdders.Select((x, y) => "[" + y + "]" + x.def.defName + "=" + x.stackCount).ToArray())
                + " cMC: " + curMagCountInt
                + " cAC: " + currentAdderCharge);

            //Because reloadOneAtATime weapons don't dump their mag at the start of a reload, have to stop the reloading process here if the mag was already full
            if (Props.reloadOneAtATime && UseAmmo && LinksMatch && CurMagCount == Props.magazineSize)
                return;

            // Issue reload job
            if (Wielder != null)
            {
                Job reloadJob = TryMakeReloadJob();
                if (reloadJob == null)
                    return;
                reloadJob.playerForced = true;
                Wielder.jobs.StartJob(reloadJob, JobCondition.InterruptForced, null, Wielder.CurJob?.def != reloadJob.def, true);
            }
        }

        /// <summary>
        /// Used to fetch a reload job for the weapon this comp is on.  Sets storedInfo to null (as if no job being replaced).
        /// </summary>
        /// <returns>Job using JobDriver_Reload</returns>
        /// <remarks>TryUnload() should be called before this in most cases.</remarks>
        public Job TryMakeReloadJob()
        {
            if (!HasMagazine || (Holder == null && turret == null))
                return null; // the job couldn't be created.

            return new Job(CE_JobDefOf.ReloadWeapon, Holder, parent);
        }

        /// <summary>Load a specified ammo Thing</summary>
        /// <param name="ammo">Specified ammo</param>
        /// <param name="largestStack">Whether to maximize stack size if called without specified ammo (somehow)</param>
        public bool LoadAmmo(Thing ammo = null, bool largestStack = false)
        {
            if (Holder == null && turret == null)
            {
                Log.Error(parent.ToString() + " tried loading ammo with no owner");
                return false;
            }
            
            if (UseAmmo)
            {
                if (ammo == null)
                {
                    if (!TryFindAmmoInInventory(CompInventory, out ammo, largestStack))
                    {
                        DoOutOfAmmoAction();
                        return false;
                    }
                }

                //Sets CurMagCount
                AddAdder(ammo);
            }
            else
            {
                CurMagCount = (Props.reloadOneAtATime) ? (curMagCountInt + 1) : Props.magazineSize;
            }
            if (turret != null) turret.isReloading = false;
            if (parent.def.soundInteract != null) parent.def.soundInteract.PlayOneShot(new TargetInfo(Position, Find.CurrentMap, false));

            return true;
        }
        
        public bool TryFindAmmoInInventory(CompInventory inventory, out Thing ammoThing, bool largestStack = false, bool setSelectedLink = false)
        {
            ammoThing = null;

            if (inventory == null)
                return false;

            // Try finding suitable ammoThing for currently set ammo first
            ammoThing = SelectedLink.BestAdder(inventory.ammoList ?? null, this, out var _, largestStack);

            if (ammoThing != null)
                return true;

            //TODO: Store currently loaded

            if (Props.reloadOneAtATime && CurMagCount > 0)
            {
                //Current mag already has a few rounds in, and the inventory doesn't have any more of that type.
                //If we let this method pick a new selectedAmmo below, it would convert the already loaded rounds to a different type,
                //so for OneAtATime weapons, we stop the process here here.
                return false;
            }

            // Try finding ammo from different type
            foreach (AmmoLink link in Props.ammoSet.ammoTypes)
            {
                if (link == SelectedLink)
                    continue;

                ammoThing = link.BestAdder(CompInventory.ammoList, this, out var _, largestStack);
                
                if (ammoThing != null)
                {
                    if (setSelectedLink)
                        SelectedLink = link;

                    return true;
                }
            }
            return false;
        }
        #endregion

        #region Unloading
      /*bool FindNewBestAdder(out int newChargeCount)
        {
            var bestThing = CurrentLink.BestAdder(adders, this, out newChargeCount, false);

            if (bestThing == null)
                return false;

            //Order newThing to front of list
            var indexOf = adders.IndexOf(bestThing);
            if (indexOf != 0)
            {
                for (int i = indexOf; i > 0; i--)
                    adders[i] = adders[i - 1];

                adders[0] = bestThing;
            }

            //Order spent thing to front of list
            
            return true;
        }*/
        
        void UnloadAdder(Thing thing = null, bool isSpent = false)
        {
            bool isCurrentAdder = false;
            if (thing == null)
            {
                if (CurrentAdder != null && CurrentAdder.stackCount > 0)
                {
                    //Important to split off only one, since everything fed to UnloadAdder is consumed
                    thing = CurrentAdder;
                    isCurrentAdder = true;
                }
            }

            if (thing == null)
                return;

            //Handle currently-used adder
            var spentThing = CurrentLink.UnloadAdder(thing, this, ref isSpent);

          //if (isCurrentAdder && currentAdderCharge > 0)
          //    currentAdderCharge = 0;

            if (spentThing == null)
            {
                if (isCurrentAdder)
                    thing = CurrentAdder.SplitOff(1);

                adders.Remove(thing);
                thing.Destroy();
                return;
            }

            //Handles CurMagCount changes
            if (!isCurrentAdder)
                AddAdder(spentThing, !isCurrentAdder && isSpent);

            //If the current thing has to be destroyed
            if (spentThing.def != thing.def)
            {
                if (isCurrentAdder)
                    thing = CurrentAdder.SplitOff(1);

                adders.Remove(thing);
                spentAdders.Remove(thing);
                thing.Destroy();
              //if (isCurrentAdder)
              //    FindNewBestAdder(out var _);
            }
        }
        
        // used by both turrets (JobDriver_ReloadTurret) and pawns (JobDriver_Reload).
        /// <summary>
        /// Used to unload the weapon.  Ammo will be dumped to the unloading Pawn's inventory or the ground if insufficient space.  Any ammo that can't be dropped
        /// on the ground is destroyed (with a warning).
        /// </summary>
        /// <returns>bool, true indicates the weapon was already in an unloaded state or the unload was successful.  False indicates an error state.</returns>
        /// <remarks>
        /// Failure to unload occurs if the weapon doesn't use a magazine.
        /// </remarks>
        public bool TryUnload(bool forceUnload = false)
        {
            return TryUnload(out var _, forceUnload, false);
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="droppedAmmo"></param>
        /// <param name="forceUnload"></param>
        /// <param name="convertAllToThingList"></param>
        /// <returns>Whether we're in "a bad state", e.g unloading failed</returns>
        public bool TryUnload(out List<Thing> droppedAmmo, bool forceUnload = false, bool convertAllToThingList = false)
        {
            droppedAmmo = new List<Thing>();

                if (!HasMagazine || (Holder == null && turret == null && !convertAllToThingList))
                    return false; // nothing to do as we are in a bad state;

                if (!UseAmmo)
                    return true; // nothing to do but we aren't in a bad state either.  Claim success.
                
                //For reloadOneAtATime weapons that haven't been explicitly told to unload, and aren't switching their ammo type, skip unloading.
                //The big advantage of a shotguns' reload mechanism is that you can add more shells without unloading the already loaded ones.
                if (Props.reloadOneAtATime && !forceUnload && !convertAllToThingList && LinksMatch && turret == null)
                    return true;

            //-- -- Add remaining ammo back in the inventory -- --
            //Returns current adder to adders or to spentAdders
            UnloadAdder();

            Log.Message("TryUnload-Pre"
                + " Adder: " + string.Join(",", adders.Select((x, y) => "[" + y + "]" + x.def.defName + "=" + x.stackCount).ToArray())
                + " Spent: " + string.Join(",", spentAdders.Select((x, y) => "[" + y + "]" + x.def.defName + "=" + x.stackCount).ToArray())
                + " cMC: " + curMagCountInt
                + " cAC: " + currentAdderCharge);

            //Clear adders
            if (curMagCountInt != 0)
            {
                //Unload adders
                for (int i = adders.Count - 1; i > -1; i--)
                {
                    var thing = adders[i];

                    //Handle remaining magcount from current adders
                    if (CurrentLink.CanAdd(thing.def, out var cpu))
                        curMagCountInt -= cpu * thing.stackCount;

                    if (convertAllToThingList)
                    {
                        droppedAmmo.Add(thing);
                        adders.RemoveAt(i);
                        continue;
                    }

                    var prevCount = thing.stackCount;

                    // Can't store ammo       || Inventory can't hold ALL ammo ...
                    if (CompInventory == null || prevCount != CompInventory.container.TryAdd(thing, thing.stackCount))
                    {
                        //.. then, drop remainder

                        // NOTE: If we get here from ThingContainer.TryAdd() it will have modified the ammoThing.stackCount to what it couldn't take.
                        if (GenThing.TryDropAndSetForbidden(thing, Position, Map, ThingPlaceMode.Near, out var droppedUnusedAmmo, turret.Faction != Faction.OfPlayer))
                            droppedAmmo.Add(droppedUnusedAmmo);
                        else
                        {
                            Log.Warning(String.Concat(this.GetType().Assembly.GetName().Name + " :: " + this.GetType().Name + " :: ",
                                                        "Unable to drop ", thing.LabelCap, " on the ground, thing was destroyed."));
                        }
                    }

                    adders.RemoveAt(i);
                }
            }

            //Clear spent adders
            if (!spentAdders.NullOrEmpty())
            {
                //Destructive, backwards iteration of spentAdders
                for (int i = spentAdders.Count - 1; i > -1; i--)
                {
                    var thing = spentAdders[i];

                    if (thing == null)
                    {
                        spentAdders.RemoveAt(i);
                        continue;
                    }

                    //Just destroy completely
                    if (CurrentLink.IsSpentAdder(thing.def))
                    {
                        thing.Destroy();
                        spentAdders.RemoveAt(i);
                        continue;
                    }

                    if (convertAllToThingList)
                    {
                        droppedAmmo.Add(thing);
                        spentAdders.RemoveAt(i);
                        continue;
                    }

                    var prevCount = thing.stackCount;

                    // Can't store ammo       || Inventory can't hold ALL ammo ...
                    if (CompInventory == null || prevCount != CompInventory.container.TryAdd(thing, thing.stackCount))
                    {
                        //.. then, drop remainder

                        // NOTE: If we get here from ThingContainer.TryAdd() it will have modified the ammoThing.stackCount to what it couldn't take.
                        if (GenThing.TryDropAndSetForbidden(thing, Position, Map, ThingPlaceMode.Near, out var droppedUnusedAmmo, turret.Faction != Faction.OfPlayer))
                            droppedAmmo.Add(droppedUnusedAmmo);
                        else
                            Log.Warning(String.Concat(this.GetType().Assembly.GetName().Name + " :: " + this.GetType().Name + " :: ",
                                                        "Unable to drop ", thing.LabelCap, " on the ground, thing was destroyed."));
                    }

                    //Just be sure it is removed from this list
                    spentAdders.RemoveAt(i);
                }
            }
            
            // Update inventory
            CompInventory?.UpdateInventory();

            return true;
        }
        #endregion

        private void DoOutOfAmmoAction()
        {
            if (ShouldThrowMote)
            {
                MoteMaker.ThrowText(Position.ToVector3Shifted(), Find.CurrentMap, "CE_OutOfAmmo".Translate() + "!");
            }
            if (Wielder != null && CompInventory != null && (Wielder.CurJob == null || Wielder.CurJob.def != JobDefOf.Hunt)) CompInventory.SwitchToNextViableWeapon();
        }

        /// <summary>
        /// Resets current ammo count to a full magazine. Intended use is pawn/turret generation where we want raiders/enemy turrets to spawn with loaded magazines. DO NOT
        /// use for regular reloads, those should be handled through LoadAmmo() instead.
        /// </summary>
        /// <param name="newAdderDef">Currently loaded ammo type will be set to this, null will load currently selected type.</param>
        public void ResetAmmoCount(ThingDef newAdderDef = null)
        {
            if (newAdderDef != null)
            {
                currentLinkInt = Props.ammoSet.ammoTypes.IndexOf(Props.ammoSet.Containing(newAdderDef));
                selectedLinkInt = currentLinkInt;
            }

            //Generate ammo if not given
            if (UseAmmo)
            {
                //Remove all stored charge information
                adders.All(x => { adders.Remove(x); x.Destroy(); return true; });
                spentAdders.All(x => { spentAdders.Remove(x); x.Destroy(); return true; });
                currentAdderCharge = 0;
                curMagCountInt = 0;

                if (newAdderDef == null)
                    newAdderDef = CurrentLink.iconAdder;

                var stackToAccountFor = CurrentLink.AmountForDeficit(newAdderDef, Props.magazineSize, true);

                //Fill adders with newAmmo
                while (stackToAccountFor > 0)
                {
                    var toLoadMag = ThingMaker.MakeThing(newAdderDef);
                    var count = Math.Min(newAdderDef.stackLimit, stackToAccountFor);
                    toLoadMag.stackCount = count;
                    stackToAccountFor -= count;
                    AddAdder(toLoadMag, false, true);
                }
            }
            else
                curMagCountInt = Props.magazineSize;

            CurMagCount = curMagCountInt;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            GizmoAmmoStatus ammoStatusGizmo = new GizmoAmmoStatus { compAmmo = this };
            yield return ammoStatusGizmo;

            if ((Wielder != null && Wielder.Faction == Faction.OfPlayer) || (turret != null && turret.Faction == Faction.OfPlayer && (turret.MannableComp != null || UseAmmo)))
            {
                Action action = null;
                if (Wielder != null) action = TryStartReload;
                else if (turret?.MannableComp != null) action = turret.TryOrderReload;

                // Check for teaching opportunities
                string tag;
                if (turret == null)
                {
                    if (HasMagazine) tag = "CE_Reload"; // Teach reloading weapons with magazines
                    else tag = "CE_ReloadNoMag";    // Teach about mag-less weapons
                }
                else
                {
                    if (turret.MannableComp == null) tag = "CE_ReloadAuto";  // Teach about auto-turrets
                    else tag = "CE_ReloadManned";    // Teach about reloading manned turrets
                }
                LessonAutoActivator.TeachOpportunity(ConceptDef.Named(tag), turret, OpportunityType.GoodToKnow);

                Command_Reload reloadCommandGizmo = new Command_Reload
                {
                    compAmmo = this,
                    action = action,
                    defaultLabel = HasMagazine ? "CE_ReloadLabel".Translate() : "",
                    defaultDesc = "CE_ReloadDesc".Translate(),
                    icon = (CurrentLink == null) ? ContentFinder<Texture2D>.Get("UI/Buttons/Reload", true) : SelectedLink.iconAdder.IconTexture(),
                    tutorTag = tag
                };
                yield return reloadCommandGizmo;
            }
        }

        public override string TransformLabel(string label)
        {
            string ammoSet = UseAmmo && Controller.settings.ShowCaliberOnGuns ? " (" + Props.ammoSet.LabelCap + ") " : "";
            return label + ammoSet;
        }
        
        /*
        public override string GetDescriptionPart()
        {
            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("CE_MagazineSize".Translate() + ": " + GenText.ToStringByStyle(Props.magazineSize, ToStringStyle.Integer));
            stringBuilder.AppendLine("CE_ReloadTime".Translate() + ": " + GenText.ToStringByStyle((Props.reloadTime), ToStringStyle.FloatTwo) + " s");
            if (UseAmmo)
            {
                // Append various ammo stats
                stringBuilder.AppendLine("CE_AmmoSet".Translate() + ": " + Props.ammoSet.LabelCap + "\n");
                foreach(var cur in Props.ammoSet.ammoTypes)
                {
                    string label = string.IsNullOrEmpty(cur.ammo.ammoClass.LabelCapShort) ? cur.ammo.ammoClass.LabelCap : cur.ammo.ammoClass.LabelCapShort;
                    stringBuilder.AppendLine(label + ":\n" + cur.projectile.GetProjectileReadout());
                }
            }
            return stringBuilder.ToString().TrimEndNewlines();
        }
        */

        #endregion Methods
    }
}
