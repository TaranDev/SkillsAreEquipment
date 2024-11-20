using BepInEx;
using BepInEx.Configuration;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using RiskOfOptions;
using RoR2;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Inventory = RoR2.Inventory;
using UnityEngine.Networking;
using static RoR2.SpawnCard;
using RoR2.Projectile;
using Facepunch.Steamworks;
using EntityStates;
using R2API;
using UnityEngine.AddressableAssets;
using RoR2.Skills;
using System.Security.Cryptography;
using Newtonsoft.Json.Utilities;
using RoR2.UI;
using static RoR2.EquipmentSlot;
using On.RoR2.Items;
using static RoR2.MasterSpawnSlotController;
using System.Security.Permissions;
using System.Security;
using EntityStates.GoldGat;
using static MonoMod.RuntimeDetour.Platforms.DetourNativeMonoPosixPlatform;

[module: UnverifiableCode]
#pragma warning disable CS0618 // Type or member is obsolete
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
#pragma warning restore CS0618 // Type or member is obsolete

[assembly: HG.Reflection.SearchableAttribute.OptIn]
namespace SkillsAreEquipment {
    [BepInDependency(R2API.ContentManagement.R2APIContentManager.PluginGUID)]
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]

    public class SkillsAreEquipment : BaseUnityPlugin {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "TaranDev";
        public const string PluginName = "SkillsAreEquipment";
        public const string PluginVersion = "1.0.0";

        public static ConfigFile config;

        ConfigEntry<KeyboardShortcut> keyBind;
        ConfigEntry<float> swapDistance;

        public float primaryFinalRechargeInterval = 0;
        public float secondaryFinalRechargeInterval = 0;
        public float utilityFinalRechargeInterval = 0;
        public float specialFinalRechargeInterval = 0;

        // The Awake() method is run at the very start when the game is initialized.
        public void Awake() {
            Log.Init(Logger);
            config = Config;
            configs();
        }

        public void configs() {
            keyBind = Config.Bind("General",
                "Unlock skill bar",
                new KeyboardShortcut(KeyCode.LeftAlt),
                "The key to hold when you want to be able to swap skills for equipment, while this key is held, skills will not fire");

            ModSettingsManager.AddOption(new KeyBindOption(keyBind));

            swapDistance = config.Bind("General", "Maximum Swap Distance", 15f, "Distance you will need to be within to swap skills with equipment. \nDefault is 15.");
            ModSettingsManager.AddOption(new StepSliderOption(swapDistance,
                new StepSliderConfig
                {
                    min = 1f,
                    max = 100f,
                    increment = 1f
                }));
        }

        // Hooks
        private void OnEnable() {
            On.RoR2.EquipmentSlot.Update += UpdateTargeting;
            On.RoR2.EquipmentSlot.UpdateTargets += EquipUpdateTargets;
            On.RoR2.EquipmentSlot.FixedUpdate += EquipFixedUpdate;
            //On.RoR2.CharacterBody.OnSkillActivated += OnSkillActivated;
            On.RoR2.GenericSkill.ExecuteIfReady += ExecuteIfReady;
            On.RoR2.GenericSkill.SetBonusStockFromBody += SetBonusStockFromBody;
            On.RoR2.GenericSkill.RestockSteplike += Restock;
            On.RoR2.GenericSkill.CalculateFinalRechargeInterval += CalculateFinalRechargeInterval;
            On.RoR2.Items.MultiShopCardUtils.OnPurchase += OnMultiShopPurchase;
            On.RoR2.CharacterMaster.OnBodyStart += OnBodyLoaded;
            On.RoR2.CharacterBody.RecalculateStats += RecalculateStats;

            On.RoR2.EquipmentSlot.UpdateGoldGat += UpdateGoldGat;
            On.EntityStates.GoldGat.BaseGoldGatState.FixedUpdate += BaseGoldGatFixedUpdate;
            On.EntityStates.GoldGat.GoldGatFire.FixedUpdate += GoldGatFireFixedUpdate;
            On.EntityStates.GoldGat.GoldGatIdle.FixedUpdate += GoldGatIdleFixedUpdate;

            //On.RoR2.EquipmentSlot.OnEquipmentExecuted += OnEquipmentExecuted;
        }

        

        // Gold Gat
        bool fireGoldGat = false;

        private void UpdateGoldGat(On.RoR2.EquipmentSlot.orig_UpdateGoldGat orig, EquipmentSlot self) {
            if (!NetworkServer.active) {
                Debug.LogWarning("[Server] function 'System.Void RoR2.EquipmentSlot::UpdateGoldGat()' called on client");
                return;
            }
            bool flag = (FindSkillEquip(self.characterBody, RoR2Content.Equipment.GoldGat.equipmentIndex) != null) || self.equipmentIndex == RoR2Content.Equipment.GoldGat.equipmentIndex;
            if (flag != (bool)self.goldgatControllerObject) {
                if (flag) {
                    self.goldgatControllerObject = UnityEngine.Object.Instantiate(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/GoldGatController"));
                    self.goldgatControllerObject.GetComponent<NetworkedBodyAttachment>().AttachToGameObjectAndSpawn(self.characterBody.gameObject);
                } else {
                    UnityEngine.Object.Destroy(self.goldgatControllerObject);
                }
            }
        }

        private void BaseGoldGatFixedUpdate(On.EntityStates.GoldGat.BaseGoldGatState.orig_FixedUpdate orig, EntityStates.GoldGat.BaseGoldGatState self) {
            self.fixedAge += Time.fixedDeltaTime;
            if (self.isAuthority) {
                if (fireGoldGat || self.characterBody.inventory.GetItemCount(RoR2Content.Items.AutoCastEquipment) > 0) {
                    self.shouldFire = true;
                } else {
                    self.shouldFire = false;
                }
            }
            //self.LinkToDisplay();
            if ((bool)self.bodyAimAnimator && (bool)self.gunAnimator) {
                self.bodyAimAnimator.UpdateAnimatorParameters(self.gunAnimator, -45f, 45f, 0f, 0f);
            }
        }

        private void GoldGatFireFixedUpdate(On.EntityStates.GoldGat.GoldGatFire.orig_FixedUpdate orig, EntityStates.GoldGat.GoldGatFire self) {

            self.fixedAge += Time.fixedDeltaTime;
            if (self.isAuthority) {
                if (fireGoldGat || self.characterBody.inventory.GetItemCount(RoR2Content.Items.AutoCastEquipment) > 0) {
                    self.shouldFire = true;
                } else {
                    self.shouldFire = false;
                }
            }
            //self.LinkToDisplay();
            if ((bool)self.bodyAimAnimator && (bool)self.gunAnimator) {
                self.bodyAimAnimator.UpdateAnimatorParameters(self.gunAnimator, -45f, 45f, 0f, 0f);
            }

            self.totalStopwatch += Time.fixedDeltaTime;
            self.stopwatch += Time.fixedDeltaTime;
            AkSoundEngine.SetRTPCValueByPlayingID(GoldGatFire.windUpRTPC, Mathf.InverseLerp(GoldGatFire.minFireFrequency, GoldGatFire.maxFireFrequency, self.fireFrequency) * 100f, self.loopSoundID);
            if (!self.CheckReturnToIdle() && self.stopwatch > 1f / self.fireFrequency) {
                self.stopwatch = 0f;
                self.FireBullet();
            }
        }

        private void GoldGatIdleFixedUpdate(On.EntityStates.GoldGat.GoldGatIdle.orig_FixedUpdate orig, EntityStates.GoldGat.GoldGatIdle self) {
            
            self.fixedAge += Time.fixedDeltaTime;
            if (self.isAuthority) {
                if (fireGoldGat || self.characterBody.inventory.GetItemCount(RoR2Content.Items.AutoCastEquipment) > 0) {
                    self.shouldFire = true;
                } else {
                    self.shouldFire = false;
                }
            }
            //self.LinkToDisplay();
            if ((bool)self.bodyAimAnimator && (bool)self.gunAnimator) {
                self.bodyAimAnimator.UpdateAnimatorParameters(self.gunAnimator, -45f, 45f, 0f, 0f);
            }

            if ((bool)self.gunAnimator) {
                self.gunAnimator.SetFloat("Crank.playbackRate", 0f, 1f, Time.fixedDeltaTime);
            }
            if (self.isAuthority && self.shouldFire && self.bodyMaster.money != 0) {
                self.outer.SetNextState(new EntityStates.GoldGat.GoldGatFire
                {
                    shouldFire = self.shouldFire
                });
            }
        }



        private void EquipUpdateTargets(On.RoR2.EquipmentSlot.orig_UpdateTargets orig, EquipmentSlot self, EquipmentIndex targetingEquipmentIndex, bool userShouldAnticipateTarget) {
            bool flag = targetingEquipmentIndex == DLC1Content.Equipment.BossHunter.equipmentIndex;
            bool flag2 = (targetingEquipmentIndex == RoR2Content.Equipment.Lightning.equipmentIndex || targetingEquipmentIndex == JunkContent.Equipment.SoulCorruptor.equipmentIndex || flag) && userShouldAnticipateTarget;
            bool flag3 = targetingEquipmentIndex == RoR2Content.Equipment.PassiveHealing.equipmentIndex && userShouldAnticipateTarget;
            bool num = flag2 || flag3;
            bool flag4 = targetingEquipmentIndex == RoR2Content.Equipment.Recycle.equipmentIndex;
            if (num) {
                if (flag2) {
                    self.ConfigureTargetFinderForEnemies();
                }
                if (flag3) {
                    self.ConfigureTargetFinderForFriendlies();
                }
                HurtBox source = null;
                if (flag) {
                    foreach (HurtBox result in self.targetFinder.GetResults()) {
                        if ((bool)result && (bool)result.healthComponent && (bool)result.healthComponent.body) {
                            DeathRewards component = result.healthComponent.body.gameObject.GetComponent<DeathRewards>();
                            if ((bool)component && (bool)component.bossDropTable && !result.healthComponent.body.HasBuff(RoR2Content.Buffs.Immune)) {
                                source = result;
                                break;
                            }
                        }
                    }
                } else {
                    source = self.targetFinder.GetResults().FirstOrDefault();
                }
                self.currentTarget = new UserTargetInfo(source);
            } else if (flag4) {
                self.currentTarget = new UserTargetInfo(self.FindPickupController(self.GetAimRay(), 10f, swapDistance.Value, requireLoS: true, targetingEquipmentIndex == RoR2Content.Equipment.Recycle.equipmentIndex));
            } else {
                self.currentTarget = default(UserTargetInfo);
            }
            GenericPickupController pickupController = self.currentTarget.pickupController;
            bool flag5 = self.currentTarget.transformToIndicateAt;
            if (flag5) {
                if (targetingEquipmentIndex == RoR2Content.Equipment.Lightning.equipmentIndex) {
                    self.targetIndicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/LightningIndicator");
                } else if (targetingEquipmentIndex == RoR2Content.Equipment.PassiveHealing.equipmentIndex) {
                    self.targetIndicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/WoodSpriteIndicator");
                } else if (targetingEquipmentIndex == RoR2Content.Equipment.Recycle.equipmentIndex) {
                    if (!pickupController.Recycled) {
                        self.targetIndicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/RecyclerIndicator");
                    } else {
                        self.targetIndicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/RecyclerBadIndicator");
                    }
                } else if (targetingEquipmentIndex == DLC1Content.Equipment.BossHunter.equipmentIndex) {
                    self.targetIndicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/BossHunterIndicator");
                } else {
                    self.targetIndicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/LightningIndicator");
                }
            }
            self.targetIndicator.active = flag5;
            self.targetIndicator.targetTransform = (flag5 ? self.currentTarget.transformToIndicateAt : null);
        }

        private void EquipFixedUpdate(On.RoR2.EquipmentSlot.orig_FixedUpdate orig, EquipmentSlot self) {
            self.UpdateInventory();
            if (NetworkServer.active) {
                EquipmentDef equipmentDef = EquipmentCatalog.GetEquipmentDef(self.equipmentIndex);
                self.subcooldownTimer -= Time.fixedDeltaTime;
                if (self.missileTimer > 0f) {
                    self.missileTimer = Mathf.Max(self.missileTimer - Time.fixedDeltaTime, 0f);
                }
                if (self.missileTimer == 0f && self.remainingMissiles > 0) {
                    self.remainingMissiles--;
                    self.missileTimer = 0.125f;
                    self.FireMissile();
                }
                self.UpdateGoldGat();
                if (self.bfgChargeTimer > 0f) {
                    self.bfgChargeTimer -= Time.fixedDeltaTime;
                    if (self.bfgChargeTimer < 0f) {
                        _ = base.transform.position;
                        Ray aimRay = self.GetAimRay();
                        Transform transform = self.FindActiveEquipmentDisplay();
                        if ((bool)transform) {
                            ChildLocator componentInChildren = transform.GetComponentInChildren<ChildLocator>();
                            if ((bool)componentInChildren) {
                                Transform transform2 = componentInChildren.FindChild("Muzzle");
                                if ((bool)transform2) {
                                    aimRay.origin = transform2.position;
                                }
                            }
                        }
                        self.healthComponent.TakeDamageForce(aimRay.direction * -1500f, alwaysApply: true);
                        ProjectileManager.instance.FireProjectile(LegacyResourcesAPI.Load<GameObject>("Prefabs/Projectiles/BeamSphere"), aimRay.origin, Util.QuaternionSafeLookRotation(aimRay.direction), base.gameObject, self.characterBody.damage * 2f, 0f, Util.CheckRoll(self.characterBody.crit, self.characterBody.master), DamageColorIndex.Item);
                        self.bfgChargeTimer = 0f;
                    }
                }
                if (equipmentDef == RoR2Content.Equipment.PassiveHealing != (bool)self.passiveHealingFollower) {
                    if (!self.passiveHealingFollower) {
                        GameObject gameObject = UnityEngine.Object.Instantiate(LegacyResourcesAPI.Load<GameObject>("Prefabs/NetworkedObjects/HealingFollower"), base.transform.position, Quaternion.identity);
                        self.passiveHealingFollower = gameObject.GetComponent<HealingFollowerController>();
                        self.passiveHealingFollower.NetworkownerBodyObject = base.gameObject;
                        NetworkServer.Spawn(gameObject);
                    } else {
                        UnityEngine.Object.Destroy(self.passiveHealingFollower.gameObject);
                        self.passiveHealingFollower = null;
                    }
                }
            }
            bool num = self.inputBank.activateEquipment.justPressed || (self.inventory?.GetItemCount(RoR2Content.Items.AutoCastEquipment) ?? 0) > 0;
            bool isEquipmentActivationAllowed = self.characterBody.isEquipmentActivationAllowed;
            if (num && isEquipmentActivationAllowed && self.hasEffectiveAuthority) {
                if (NetworkServer.active) {
                    self.ExecuteIfReady();
                } else {
                    self.CallCmdExecuteIfReady();
                }
            }

            
        }

        private bool ExecuteIfReady(On.RoR2.GenericSkill.orig_ExecuteIfReady orig, GenericSkill self) {
            var body = PlayerCharacterMasterController.instances[0].master.GetBody();

            EquipmentIndex currentSkillIndex = EquipmentCatalog.FindEquipmentIndex(self.skillDef.skillName);

            if (Input.GetKey(keyBind.Value.MainKey)) {
                return false;
            } else if (currentSkillIndex == DLC1Content.Equipment.MultiShopCard.equipmentIndex) {
                return false;
            } else if (currentSkillIndex == RoR2Content.Equipment.Lightning.equipmentIndex && lightningIndicator.targetTransform == null) {
                return false;
            } else if (currentSkillIndex == RoR2Content.Equipment.Recycle.equipmentIndex && recyclerIndicator.targetTransform == null) {
                return false;
            } else if (currentSkillIndex == DLC1Content.Equipment.BossHunter.equipmentIndex && gunIndicator.targetTransform == null) {
                return false;
            } else if (currentSkillIndex == DLC1Content.Equipment.BossHunter.equipmentIndex) {
                bool outcome = orig(self);
                if (outcome) {
                    SkillDef def;
                    equipmentSkillDefs.TryGetValue(DLC1Content.Equipment.BossHunterConsumed.equipmentIndex, out def);
                    self.AssignSkill(def);

                    EquipmentSlot eq = self.characterBody.equipmentSlot;

                    eq.UpdateTargets(DLC1Content.Equipment.BossHunter.equipmentIndex, userShouldAnticipateTarget: true);
                    HurtBox hurtBox = eq.currentTarget.hurtBox;
                    DeathRewards deathRewards = hurtBox?.healthComponent?.body?.gameObject?.GetComponent<DeathRewards>();
                    if ((bool)hurtBox && (bool)deathRewards) {
                        Vector3 vector = (hurtBox.transform ? hurtBox.transform.position : Vector3.zero);
                        Vector3 normalized = (vector - eq.characterBody.corePosition).normalized;
                        PickupDropletController.CreatePickupDroplet(deathRewards.bossDropTable.GenerateDrop(eq.rng), vector, normalized * 15f);
                        if ((bool)hurtBox?.healthComponent?.body?.master) {
                            hurtBox.healthComponent.body.master.TrueKill(base.gameObject);
                        }
                        CharacterModel component = hurtBox.hurtBoxGroup.GetComponent<CharacterModel>();
                        if ((bool)component) {
                            TemporaryOverlay temporaryOverlay = component.gameObject.AddComponent<TemporaryOverlay>();
                            temporaryOverlay.duration = 0.1f;
                            temporaryOverlay.animateShaderAlpha = true;
                            temporaryOverlay.alphaCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
                            temporaryOverlay.destroyComponentOnEnd = true;
                            temporaryOverlay.originalMaterial = LegacyResourcesAPI.Load<Material>("Materials/matHuntressFlashBright");
                            temporaryOverlay.AddToCharacerModel(component);
                            TemporaryOverlay temporaryOverlay2 = component.gameObject.AddComponent<TemporaryOverlay>();
                            temporaryOverlay2.duration = 1.2f;
                            temporaryOverlay2.animateShaderAlpha = true;
                            temporaryOverlay2.alphaCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
                            temporaryOverlay2.destroyComponentOnEnd = true;
                            temporaryOverlay2.originalMaterial = LegacyResourcesAPI.Load<Material>("Materials/matGhostEffect");
                            temporaryOverlay2.AddToCharacerModel(component);
                        }
                        DamageInfo damageInfo = new DamageInfo();
                        damageInfo.attacker = base.gameObject;
                        damageInfo.force = -normalized * 2500f;
                        eq.healthComponent.TakeDamageForce(damageInfo, alwaysApply: true);
                        GameObject effectPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/BossHunterKillEffect");
                        Quaternion rotation = Util.QuaternionSafeLookRotation(normalized, Vector3.up);
                        EffectManager.SpawnEffect(effectPrefab, new EffectData
                        {
                            origin = vector,
                            rotation = rotation
                        }, transmit: true);
                        CharacterModel characterModel = base.gameObject.GetComponent<ModelLocator>()?.modelTransform?.GetComponent<CharacterModel>();
                        if ((bool)characterModel) {
                            foreach (GameObject equipmentDisplayObject in characterModel.GetEquipmentDisplayObjects(DLC1Content.Equipment.BossHunter.equipmentIndex)) {
                                if (equipmentDisplayObject.name.Contains("DisplayTricorn")) {
                                    EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/BossHunterHatEffect"), new EffectData
                                    {
                                        origin = equipmentDisplayObject.transform.position,
                                        rotation = equipmentDisplayObject.transform.rotation,
                                        scale = equipmentDisplayObject.transform.localScale.x
                                    }, transmit: true);
                                } else {
                                    EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/BossHunterGunEffect"), new EffectData
                                    {
                                        origin = equipmentDisplayObject.transform.position,
                                        rotation = Util.QuaternionSafeLookRotation(vector - equipmentDisplayObject.transform.position, Vector3.up),
                                        scale = equipmentDisplayObject.transform.localScale.x
                                    }, transmit: true);
                                }
                            }
                        }
                        eq.InvalidateCurrentTarget();
                    }
                }
                return outcome;
            } else if (currentSkillIndex == RoR2Content.Equipment.GoldGat.equipmentIndex) {
                bool outcome = orig(self);
                if(outcome) {
                    fireGoldGat = !fireGoldGat;
                }
                return outcome;
            } else {
                return orig(self);
            }
        }

        private void RecalculateStats(On.RoR2.CharacterBody.orig_RecalculateStats orig, CharacterBody self) {
            orig(self);
            if ((bool)self.skillLocator.primary) {
                self.skillLocator.primaryBonusStockSkill.SetBonusStockFromBody(0);
            }
        }

        private void OnBodyLoaded(On.RoR2.CharacterMaster.orig_OnBodyStart orig, CharacterMaster self, CharacterBody body) {

            if (self == PlayerCharacterMasterController.instances[0].master) {
                primaryFinalRechargeInterval = 0;
                secondaryFinalRechargeInterval = 0;
                utilityFinalRechargeInterval = 0;
                specialFinalRechargeInterval = 0;

                primaryLastTimeSwitched = 0;
                secondaryLastTimeSwitched = 0;
                utilityLastTimeSwitched = 0;
                specialLastTimeSwitched = 0;

                equipLastTimePressed = 0;

                fireGoldGat = false;

                lightningIndicator.targetTransform = null;
                recyclerIndicator.targetTransform = null;
                gunIndicator.targetTransform = null;
                woodspriteIndicator.targetTransform = null;

                orig(self, body);

                if (oldBasePrimary) {
                    body.skillLocator.primary.SetBaseSkill(oldBasePrimary);
                }
                if (oldBaseSecondary) {
                    body.skillLocator.secondary.SetBaseSkill(oldBaseSecondary);
                }
                if (oldBaseUtility) {
                    body.skillLocator.utility.SetBaseSkill(oldBaseUtility);
                }
                if (oldBaseSpecial) {
                    body.skillLocator.special.SetBaseSkill(oldBaseSpecial);
                }
            } else {
                orig(self, body);
            }
        }

        private void OnDisable() {
            On.RoR2.EquipmentSlot.Update -= UpdateTargeting;
            On.RoR2.EquipmentSlot.UpdateTargets -= EquipUpdateTargets;
            On.RoR2.EquipmentSlot.FixedUpdate -= EquipFixedUpdate;
            //On.RoR2.CharacterBody.OnSkillActivated -= OnSkillActivated;
            On.RoR2.GenericSkill.ExecuteIfReady -= ExecuteIfReady;
            On.RoR2.GenericSkill.SetBonusStockFromBody -= SetBonusStockFromBody;
            On.RoR2.GenericSkill.RestockSteplike += Restock;
            On.RoR2.GenericSkill.CalculateFinalRechargeInterval -= CalculateFinalRechargeInterval;
            On.RoR2.Items.MultiShopCardUtils.OnPurchase -= OnMultiShopPurchase;
            On.RoR2.CharacterMaster.OnBodyStart -= OnBodyLoaded;
            On.RoR2.CharacterBody.RecalculateStats -= RecalculateStats;

            On.RoR2.EquipmentSlot.UpdateGoldGat -= UpdateGoldGat;
            On.EntityStates.GoldGat.BaseGoldGatState.FixedUpdate -= BaseGoldGatFixedUpdate;
            On.EntityStates.GoldGat.GoldGatFire.FixedUpdate -= GoldGatFireFixedUpdate;
            On.EntityStates.GoldGat.GoldGatIdle.FixedUpdate -= GoldGatIdleFixedUpdate;
            //On.RoR2.EquipmentSlot.OnEquipmentExecuted -= OnEquipmentExecuted;
        }

        private void OnMultiShopPurchase(MultiShopCardUtils.orig_OnPurchase orig, CostTypeDef.PayCostContext context, int moneyCost) {
            CharacterMaster activatorMaster = context.activatorMaster;
            if (!activatorMaster || !activatorMaster.hasBody || !activatorMaster.inventory || !FindSkillEquip(activatorMaster.GetBody(), DLC1Content.Equipment.MultiShopCard.equipmentIndex)) {
                return;
            }
            CharacterBody body = activatorMaster.GetBody();
            bool flag = false;
            if (moneyCost > 0) {
                flag = true;
                RoR2.Orbs.GoldOrb goldOrb = new RoR2.Orbs.GoldOrb();
                goldOrb.origin = context.purchasedObject?.transform?.position ?? body.corePosition;
                goldOrb.target = body.mainHurtBox;
                goldOrb.goldAmount = (uint)(0.1f * (float)moneyCost);
                RoR2.Orbs.OrbManager.instance.AddOrb(goldOrb);
            }
            ShopTerminalBehavior shopTerminalBehavior = context.purchasedObject?.GetComponent<ShopTerminalBehavior>();
            if ((bool)shopTerminalBehavior && (bool)shopTerminalBehavior.serverMultiShopController) {
                flag = true;
                shopTerminalBehavior.serverMultiShopController.SetCloseOnTerminalPurchase(context.purchasedObject.GetComponent<PurchaseInteraction>(), doCloseMultiShop: false);
            }
            if (flag) {
                if (body.hasAuthority) {
                    FireCardSkillEquip(body);
                }
            }
        }

        public bool FireCardSkillEquip(CharacterBody player) {
            foreach (GenericSkill skill in player.skillLocator.allSkills) {
                if (EquipmentCatalog.FindEquipmentIndex(skill.skillDef.skillName) == DLC1Content.Equipment.MultiShopCard.equipmentIndex) {
                    FireBottledChaos();
                }
            }
            return false;
        }

        public void FireBottledChaos() {
            CharacterBody characterBody = PlayerCharacterMasterController.instances[0].master.GetBody();
            Inventory inventory = characterBody.inventory;

            int itemCount = inventory.GetItemCount(RoR2Content.Items.EnergizedOnEquipmentUse);
            if (itemCount > 0) {
                characterBody.AddTimedBuff(RoR2Content.Buffs.Energized, 8 + 4 * (itemCount - 1));
            }
            int itemCount2 = inventory.GetItemCount(DLC1Content.Items.RandomEquipmentTrigger);
            if (itemCount2 <= 0 || EquipmentCatalog.randomTriggerEquipmentList.Count <= 0) {
                return;
            }
            List<EquipmentIndex> list = new List<EquipmentIndex>(EquipmentCatalog.randomTriggerEquipmentList);
            if (inventory.currentEquipmentIndex != EquipmentIndex.None) {
                list.Remove(inventory.currentEquipmentIndex);
            }
            Util.ShuffleList(list, characterBody.equipmentSlot.rng);
            if (inventory.currentEquipmentIndex != EquipmentIndex.None) {
                list.Add(inventory.currentEquipmentIndex);
            }
            int num = 0;
            bool flag = false;
            bool flag2 = false;
            for (int i = 0; i < itemCount2; i++) {
                if (flag2) {
                    break;
                }
                EquipmentIndex equipmentIndex = EquipmentIndex.None;
                do {
                    if (num >= list.Count) {
                        if (!flag) {
                            flag2 = true;
                            break;
                        }
                        flag = false;
                        num %= list.Count;
                    }
                    equipmentIndex = list[num];
                    num++;
                }
                while (!characterBody.equipmentSlot.PerformEquipmentAction(EquipmentCatalog.GetEquipmentDef(equipmentIndex)));
                if (equipmentIndex == RoR2Content.Equipment.BFG.equipmentIndex) {
                    ModelLocator component = GetComponent<ModelLocator>();
                    if ((bool)component) {
                        Transform modelTransform = component.modelTransform;
                        if ((bool)modelTransform) {
                            CharacterModel component2 = modelTransform.GetComponent<CharacterModel>();
                            if ((bool)component2) {
                                List<GameObject> itemDisplayObjects = component2.GetItemDisplayObjects(DLC1Content.Items.RandomEquipmentTrigger.itemIndex);
                                if (itemDisplayObjects.Count > 0) {
                                    UnityEngine.Object.Instantiate(Addressables.LoadAssetAsync<GameObject>("RoR2/Base/BFG/ChargeBFG.prefab").WaitForCompletion(), itemDisplayObjects[0].transform);
                                }
                            }
                        }
                    }
                }
                flag = true;
            }
            EffectData effectData = new EffectData();
            effectData.origin = characterBody.corePosition;
            effectData.SetNetworkedObjectReference(base.gameObject);
            EffectManager.SpawnEffect(LegacyResourcesAPI.Load<GameObject>("Prefabs/Effects/RandomEquipmentTriggerProcEffect"), effectData, transmit: true);
        }

        private void Restock(On.RoR2.GenericSkill.orig_RestockSteplike orig, GenericSkill self) {
            if (self == self.characterBody.skillLocator.primary) {
                if (primaryFinalRechargeInterval > 0) {
                    primaryFinalRechargeInterval = -1;
                    self.RecalculateFinalRechargeInterval();
                }
            }
            if (self == self.characterBody.skillLocator.secondary) {
                if (secondaryFinalRechargeInterval > 0) {
                    secondaryFinalRechargeInterval = -1;
                    self.RecalculateFinalRechargeInterval();
                }
            }
            if (self == self.characterBody.skillLocator.utility) {
                if (utilityFinalRechargeInterval > 0) {
                    utilityFinalRechargeInterval = -1;
                    self.RecalculateFinalRechargeInterval();
                }
            }
            if (self == self.characterBody.skillLocator.special) {
                if (specialFinalRechargeInterval > 0) {
                    specialFinalRechargeInterval = -1;
                    self.RecalculateFinalRechargeInterval();
                }
            }

            orig(self);
            
        }

        private void SetBonusStockFromBody(On.RoR2.GenericSkill.orig_SetBonusStockFromBody orig, GenericSkill self, int newBonusStockFromBody) {
            EquipmentIndex equip = EquipmentCatalog.FindEquipmentIndex(self.skillDef.skillName);
            if (equipmentSkillDefs.ContainsKey(equip)) {
                CharacterBody playerBody = self.characterBody;
                Inventory inventory = playerBody.inventory;
                int numFuelCells = inventory.GetItemCount(RoR2Content.Items.EquipmentMagazine);
                int numGestures = inventory.GetItemCount(RoR2Content.Items.AutoCastEquipment);

                if (numGestures > 0) {
                    // Has Gesture Formula
                    self.cooldownScale = (float)0.5 * (float)(Math.Pow((1 - 0.15), (numGestures + numFuelCells - 1)));
                } else {
                    // Has No Gesture Formula
                    self.cooldownScale = (float)Math.Pow((1 - 0.15), (numFuelCells));
                }

                newBonusStockFromBody = newBonusStockFromBody + numFuelCells;
            }
            orig(self, newBonusStockFromBody);
        }

        /*private void OnSkillActivated(On.RoR2.CharacterBody.orig_OnSkillActivated orig, CharacterBody self, GenericSkill skill) {
            var body = PlayerCharacterMasterController.instances[0].master.GetBody();

            EquipmentIndex currentSkillIndex = EquipmentCatalog.FindEquipmentIndex(skill.skillDef.skillName);

            if (Input.GetKey(keyBind.Value.MainKey)) {
                return;
            } else if (currentSkillIndex == DLC1Content.Equipment.MultiShopCard.equipmentIndex) {
                return;
            } else if (currentSkillIndex == RoR2Content.Equipment.Lightning.equipmentIndex && lightningIndicator.targetTransform == null) {
                return;
            } else if (currentSkillIndex == RoR2Content.Equipment.Recycle.equipmentIndex && recyclerIndicator.targetTransform == null) {
                return;
            } else if (currentSkillIndex == DLC1Content.Equipment.BossHunter.equipmentIndex && gunIndicator.targetTransform == null) {
                return;
            } else if (currentSkillIndex == RoR2Content.Equipment.PassiveHealing.equipmentIndex && woodspriteIndicator.targetTransform == null) {
                return;
            } else {
                orig(self, skill);
            }
        }*/

        private float CalculateFinalRechargeInterval(On.RoR2.GenericSkill.orig_CalculateFinalRechargeInterval orig, GenericSkill self) {

            if (self == self.characterBody.skillLocator.primary && primaryFinalRechargeInterval > 0) {
                return primaryFinalRechargeInterval;
            } else if (self == self.characterBody.skillLocator.secondary && secondaryFinalRechargeInterval > 0) {
                return secondaryFinalRechargeInterval;
            } else if (self == self.characterBody.skillLocator.utility && utilityFinalRechargeInterval > 0) {
                return utilityFinalRechargeInterval;
            } else if (self == self.characterBody.skillLocator.special && specialFinalRechargeInterval > 0) {
                return specialFinalRechargeInterval;
            }

            return orig(self);
        }

        private Indicator lightningIndicator = null;
        private BullseyeSearch lightningTargetFinder = new BullseyeSearch();
        private Indicator recyclerIndicator = null;
        private BullseyeSearch recyclerTargetFinder = new BullseyeSearch();
        private Indicator gunIndicator = null;
        private BullseyeSearch gunTargetFinder = new BullseyeSearch();
        private Indicator woodspriteIndicator = null;
        private BullseyeSearch woodspriteTargetFinder = new BullseyeSearch();

        public void UpdateIndicators() {
            CharacterBody player = PlayerCharacterMasterController.instances[0].master.GetBody();

            if (lightningIndicator == null) {
                lightningIndicator = new Indicator(player.gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/LightningIndicator"));
                lightningIndicator.active = true;
            } else if (lightningIndicator.owner != player.gameObject) {
                lightningIndicator.active = false;
                lightningIndicator = new Indicator(player.gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/LightningIndicator"));
                lightningIndicator.active = true;
            }

            if (recyclerIndicator == null) {
                recyclerIndicator = new Indicator(player.gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/RecyclerIndicator"));
                recyclerIndicator.active = true;
            } else if (recyclerIndicator.owner != player.gameObject) {
                recyclerIndicator.active = false;
                recyclerIndicator = new Indicator(player.gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/RecyclerIndicator"));
                recyclerIndicator.active = true;
            }

            if (gunIndicator == null) {
                gunIndicator = new Indicator(player.gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/BossHunterIndicator"));
                gunIndicator.active = true;
            } else if (gunIndicator.owner != player.gameObject) {
                gunIndicator.active = false;
                gunIndicator = new Indicator(player.gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/BossHunterIndicator"));
                gunIndicator.active = true;
            }

            if (woodspriteIndicator == null) {
                woodspriteIndicator = new Indicator(player.gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/WoodSpriteIndicator"));
                woodspriteIndicator.active = true;
            } else if (woodspriteIndicator.owner != player.gameObject) {
                woodspriteIndicator.active = false;
                woodspriteIndicator = new Indicator(player.gameObject, LegacyResourcesAPI.Load<GameObject>("Prefabs/WoodSpriteIndicator"));
                woodspriteIndicator.active = true;
            }
            GenericSkill playerLightning = FindSkillEquip(player, RoR2Content.Equipment.Lightning.equipmentIndex);
            if (playerLightning && playerLightning.stock > 0 || player.equipmentSlot.equipmentIndex == RoR2Content.Equipment.Lightning.equipmentIndex && player.equipmentSlot.stock > 0) {
                lightningIndicator.active = true;
                lightningIndicator.targetTransform = FindTarget(player, lightningIndicator, lightningTargetFinder, RoR2Content.Equipment.Lightning.equipmentIndex);
            } else {
                lightningIndicator.active = false;
                lightningIndicator.targetTransform = null;
            }
            GenericSkill playerRecycler = FindSkillEquip(player, RoR2Content.Equipment.Recycle.equipmentIndex);
            if (playerRecycler && playerRecycler.stock > 0 || player.equipmentSlot.equipmentIndex == RoR2Content.Equipment.Recycle.equipmentIndex && player.equipmentSlot.stock > 0) {
                recyclerIndicator.active = true;
                recyclerIndicator.targetTransform = FindTarget(player, recyclerIndicator, recyclerTargetFinder, RoR2Content.Equipment.Recycle.equipmentIndex);
            } else {
                recyclerIndicator.active = false;
                recyclerIndicator.targetTransform = null;
            }
            GenericSkill playerGun = FindSkillEquip(player, DLC1Content.Equipment.BossHunter.equipmentIndex);
            if (playerGun && playerGun.stock > 0 || player.equipmentSlot.equipmentIndex == DLC1Content.Equipment.BossHunter.equipmentIndex && player.equipmentSlot.stock > 0) {
                gunIndicator.active = true;
                gunIndicator.targetTransform = FindTarget(player, gunIndicator, gunTargetFinder, DLC1Content.Equipment.BossHunter.equipmentIndex);
            } else {
                gunIndicator.active = false;
                gunIndicator.targetTransform = null;
            }
            GenericSkill playerWoodsprite = FindSkillEquip(player, RoR2Content.Equipment.PassiveHealing.equipmentIndex);
            if (playerWoodsprite && playerWoodsprite.stock > 0 || player.equipmentSlot.equipmentIndex == RoR2Content.Equipment.PassiveHealing.equipmentIndex && player.equipmentSlot.stock > 0) {
                woodspriteIndicator.active = true;
                woodspriteIndicator.targetTransform = FindTarget(player, woodspriteIndicator, woodspriteTargetFinder, RoR2Content.Equipment.PassiveHealing.equipmentIndex);
            } else {
                woodspriteIndicator.active = false;
                woodspriteIndicator.targetTransform = null;
            }
        }

        public GenericSkill FindSkillEquip(CharacterBody player, EquipmentIndex equipmentIndex) {
            foreach (GenericSkill skill in player.skillLocator.allSkills) {
                if (EquipmentCatalog.FindEquipmentIndex(skill.skillDef.skillName) == equipmentIndex) {
                    return skill;
                }
            }
            return null;
        }

        public Transform FindTarget(CharacterBody player, Indicator indicator, BullseyeSearch targetFinder, EquipmentIndex targetingEquipmentIndex) {
            EquipmentSlot.UserTargetInfo currentTarget;
            bool flag = targetingEquipmentIndex == DLC1Content.Equipment.BossHunter.equipmentIndex;
            bool flag2 = (targetingEquipmentIndex == RoR2Content.Equipment.Lightning.equipmentIndex || targetingEquipmentIndex == JunkContent.Equipment.SoulCorruptor.equipmentIndex || flag);
            bool flag3 = targetingEquipmentIndex == RoR2Content.Equipment.PassiveHealing.equipmentIndex;
            bool num = flag2 || flag3;
            bool flag4 = targetingEquipmentIndex == RoR2Content.Equipment.Recycle.equipmentIndex;
            if (num) {
                if (flag2) {
                    targetFinder = ConfigureTargetFinderForEnemies(player, targetFinder);
                }
                if (flag3) {
                    targetFinder = ConfigureTargetFinderForFriendlies(player, targetFinder);
                }
                HurtBox source = null;
                if (flag) {
                    foreach (HurtBox result in targetFinder.GetResults()) {
                        if ((bool)result && (bool)result.healthComponent && (bool)result.healthComponent.body) {
                            DeathRewards component = result.healthComponent.body.gameObject.GetComponent<DeathRewards>();
                            if ((bool)component && (bool)component.bossDropTable && !result.healthComponent.body.HasBuff(RoR2Content.Buffs.Immune)) {
                                source = result;
                                break;
                            }
                        }
                    }
                } else {
                    source = targetFinder.GetResults().FirstOrDefault();
                }
                currentTarget = new UserTargetInfo(source);
            } else if (flag4) {
                currentTarget = new UserTargetInfo(player.equipmentSlot.FindPickupController(GetAimRay(player), 10f, 30f, requireLoS: true, targetingEquipmentIndex == RoR2Content.Equipment.Recycle.equipmentIndex));
            } else {
                currentTarget = default(UserTargetInfo);
            }
            GenericPickupController pickupController = currentTarget.pickupController;
            bool flag5 = currentTarget.transformToIndicateAt;
            if (flag5) {
                if (targetingEquipmentIndex == RoR2Content.Equipment.Lightning.equipmentIndex) {
                    indicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/LightningIndicator");
                } else if (targetingEquipmentIndex == RoR2Content.Equipment.PassiveHealing.equipmentIndex) {
                    indicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/WoodSpriteIndicator");
                } else if (targetingEquipmentIndex == RoR2Content.Equipment.Recycle.equipmentIndex) {
                    if (!pickupController.Recycled) {
                        indicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/RecyclerIndicator");
                    } else {
                        indicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/RecyclerBadIndicator");
                    }
                } else if (targetingEquipmentIndex == DLC1Content.Equipment.BossHunter.equipmentIndex) {
                    indicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/BossHunterIndicator");
                } else {
                    indicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/LightningIndicator");
                }
            }
            return (flag5 ? currentTarget.transformToIndicateAt : null);
        }

        protected void UpdateTargeting(On.RoR2.EquipmentSlot.orig_Update orig, EquipmentSlot self) {
            orig(self);

            // Equipment skill replace selector code
            self.UpdateTargets(RoR2Content.Equipment.Recycle.equipmentIndex, userShouldAnticipateTarget: true);
            if (self.currentTarget.pickupController != null) {
                PickupDef pickupDef = PickupCatalog.GetPickupDef(self.currentTarget.pickupController.pickupIndex);
                self.targetIndicator.visualizerPrefab = LegacyResourcesAPI.Load<GameObject>("Prefabs/RecyclerIndicator");
                if (pickupDef != null && pickupDef.equipmentIndex != EquipmentIndex.None) {
                    self.targetIndicator.active = true;
                } else {
                    self.targetIndicator.active = false;
                }
            }
        }

        private Ray GetAimRay(CharacterBody player) {
            Ray result = default(Ray);
            result.direction = player.inputBank.aimDirection;
            result.origin = player.inputBank.aimOrigin;
            return result;
        }

        private BullseyeSearch ConfigureTargetFinderBase(CharacterBody player, BullseyeSearch targetFinder) {
            targetFinder.teamMaskFilter = TeamMask.allButNeutral;
            targetFinder.teamMaskFilter.RemoveTeam(player.teamComponent.teamIndex);
            targetFinder.sortMode = BullseyeSearch.SortMode.Angle;
            targetFinder.filterByLoS = true;
            float extraRaycastDistance;
            Ray ray = CameraRigController.ModifyAimRayIfApplicable(GetAimRay(player), base.gameObject, out extraRaycastDistance);
            targetFinder.searchOrigin = ray.origin;
            targetFinder.searchDirection = ray.direction;
            targetFinder.maxAngleFilter = 10f;
            targetFinder.viewer = player;
            return targetFinder;
        }

        private BullseyeSearch ConfigureTargetFinderForEnemies(CharacterBody player, BullseyeSearch targetFinder) {
            targetFinder = ConfigureTargetFinderBase(player, targetFinder);
            targetFinder.teamMaskFilter = TeamMask.GetUnprotectedTeams(player.teamComponent.teamIndex);
            targetFinder.RefreshCandidates();
            targetFinder.FilterOutGameObject(base.gameObject);
            return targetFinder;
        }

        private BullseyeSearch ConfigureTargetFinderForBossesWithRewards(CharacterBody player, BullseyeSearch targetFinder) {
            targetFinder = ConfigureTargetFinderBase(player, targetFinder);
            targetFinder.teamMaskFilter = TeamMask.GetUnprotectedTeams(player.teamComponent.teamIndex);
            targetFinder.RefreshCandidates();
            targetFinder.FilterOutGameObject(base.gameObject);
            return targetFinder;
        }

        private BullseyeSearch ConfigureTargetFinderForFriendlies(CharacterBody player, BullseyeSearch targetFinder) {
            targetFinder = ConfigureTargetFinderBase(player, targetFinder);
            targetFinder.teamMaskFilter = TeamMask.none;
            targetFinder.teamMaskFilter.AddTeam(player.teamComponent.teamIndex);
            targetFinder.RefreshCandidates();
            targetFinder.FilterOutGameObject(base.gameObject);
            return targetFinder;
        }

        public static Dictionary<EquipmentIndex, SkillDef> equipmentSkillDefs = new Dictionary<EquipmentIndex, SkillDef>();

        [SystemInitializer(typeof(EquipmentCatalog))]
        private static void skillInit() {

            SkillCreator creator = new SkillCreator();
            Log.Info("Equipment Defs: " + EquipmentCatalog.equipmentDefs);
            foreach (EquipmentDef def in EquipmentCatalog.equipmentDefs) {
                try {
                    Log.Info("Initialising Primary Equipment Skill: " + def.name);
                    equipmentSkillDefs.Add(def.equipmentIndex, creator.CreateSkillFromEquipment(def));
                } catch (Exception e) {

                }
            }
        }

        float primaryLastTimeSwitched = 0;
        float secondaryLastTimeSwitched = 0;
        float utilityLastTimeSwitched = 0;
        float specialLastTimeSwitched = 0;
        float equipLastTimePressed = 0;

        SkillDef oldBasePrimary;
        SkillDef oldBaseSecondary;
        SkillDef oldBaseUtility;
        SkillDef oldBaseSpecial;

        public void assignEquipSkill(CharacterBody body, GenericSkill skill) {
            EquipmentIndex currentSkillIndex = EquipmentCatalog.FindEquipmentIndex(skill.skillDef.skillName);
            
            
            GenericPickupController target = body.equipmentSlot.currentTarget.pickupController;
            if (target != null && Input.GetKey(keyBind.Value.MainKey)) {
                PickupDef pickupDef = PickupCatalog.GetPickupDef(target.pickupIndex);
                if (pickupDef.equipmentIndex != EquipmentIndex.None) {
                    SkillDef def;
                    equipmentSkillDefs.TryGetValue(pickupDef.equipmentIndex, out def);
                    if (def != null) {

                        int oldStock = skill.stock;

                        if (skill == body.skillLocator.primary) {

                            if (Run.TimeStamp.tNow - primaryLastTimeSwitched > 0.1) {
                                primaryLastTimeSwitched = Run.TimeStamp.tNow;
                            } else {
                                return;
                            }
                            if (skill.cooldownRemaining > 0) {
                                primaryFinalRechargeInterval = skill.finalRechargeInterval;
                            }

                            oldBasePrimary = def;
                        } else if (skill == body.skillLocator.secondary) {

                            if (Run.TimeStamp.tNow - secondaryLastTimeSwitched > 0.1) {
                                secondaryLastTimeSwitched = Run.TimeStamp.tNow;
                            } else {
                                return;
                            }
                            if (skill.cooldownRemaining > 0) {
                                secondaryLastTimeSwitched = skill.finalRechargeInterval;
                            }

                            oldBaseSecondary = def;
                        } else if (skill == body.skillLocator.utility) {

                            if (Run.TimeStamp.tNow - utilityLastTimeSwitched > 0.1) {
                                utilityLastTimeSwitched = Run.TimeStamp.tNow;
                            } else {
                                return;
                            }
                            if (skill.cooldownRemaining > 0) {
                                utilityLastTimeSwitched = skill.finalRechargeInterval;
                            }
                            
                            oldBaseUtility = def;
                        } else if (skill == body.skillLocator.special) {

                            if (Run.TimeStamp.tNow - specialLastTimeSwitched > 0.1) {
                                specialLastTimeSwitched = Run.TimeStamp.tNow;
                            } else {
                                return;
                            }
                            if (skill.cooldownRemaining > 0) {
                                specialLastTimeSwitched = skill.finalRechargeInterval;
                            }

                            oldBaseSpecial = def;
                        }

                        if (currentSkillIndex != EquipmentIndex.None) {
                            if (currentSkillIndex == RoR2Content.Equipment.GoldGat.equipmentIndex) {
                                fireGoldGat = false;
                            }
                            target.SyncPickupIndex(PickupCatalog.FindPickupIndex(currentSkillIndex));
                        } else {
                            NetworkServer.Destroy(target.gameObject);
                        }

                        skill.SetBaseSkill(def);

                        skill.stock = oldStock;

                    }
                }
            }
        }



        public float healingInterval = 1f;
        private float healingTimer = 0;
        public float fractionHealthHealing = 0.01f;

        private void Update() {

            if (Run.instance != null) {
                UpdateIndicators();
                CharacterBody player = PlayerCharacterMasterController.instances[0].master.GetBody();

                // Gesture
                if (player && player.inventory && player.inventory.GetItemCount(RoR2Content.Items.AutoCastEquipment.itemIndex) > 0) {
                    foreach (GenericSkill skill in player.skillLocator.allSkills) {
                        EquipmentIndex equip = EquipmentCatalog.FindEquipmentIndex(skill.skillDef.skillName);
                        if (equipmentSkillDefs.ContainsKey(equip) && skill.stock > 0) {
                            skill.ExecuteIfReady();
                        }
                    }
                }

                if (player) {
                    if (player.inputBank.skill1.justReleased) {
                        assignEquipSkill(player, player.skillLocator.primary);
                    }

                    if (player.inputBank.skill2.justReleased) {
                        assignEquipSkill(player, player.skillLocator.secondary);
                    }

                    if (player.inputBank.skill3.justReleased) {
                        assignEquipSkill(player, player.skillLocator.utility);
                    }

                    if (player.inputBank.skill4.justReleased) {
                        assignEquipSkill(player, player.skillLocator.special);
                    }

                    // Gold Gat
                    if (player.inputBank.activateEquipment.justPressed && player.equipmentSlot.equipmentIndex == RoR2Content.Equipment.GoldGat.equipmentIndex) {

                        if (Run.TimeStamp.tNow - equipLastTimePressed > 0.1) {
                            equipLastTimePressed = Run.TimeStamp.tNow;
                            fireGoldGat = !fireGoldGat;
                        }
                    }

                    if(FindSkillEquip(player, RoR2Content.Equipment.PassiveHealing.equipmentIndex)) {
                        healingTimer -= Time.fixedDeltaTime;
                        if (healingTimer <= 0f) {
                            healingTimer = healingInterval;
                            player.healthComponent.HealFraction(fractionHealthHealing * healingInterval, default(ProcChainMask));
                        }
                    }
                } 
            }
            if (Run.instance == null && (oldBasePrimary != null || oldBaseSecondary != null || oldBaseUtility != null || oldBaseSpecial != null)) {
                oldBasePrimary = null;
                oldBaseSecondary = null;
                oldBaseUtility = null;
                oldBaseSpecial = null;
            }
            if (Run.instance == null && (lightningIndicator != null || recyclerIndicator != null || gunIndicator != null || woodspriteIndicator != null)) {
                lightningIndicator = null;
                lightningTargetFinder = new BullseyeSearch();
                recyclerIndicator = null;
                recyclerTargetFinder = new BullseyeSearch();
                gunIndicator = null;
                gunTargetFinder = new BullseyeSearch();
                woodspriteIndicator = null;
                woodspriteTargetFinder = new BullseyeSearch();
            }
        }
    }
}
