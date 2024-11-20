using BepInEx;
using EntityStates;
using R2API;
using RoR2;
using RoR2.Skills;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace SkillsAreEquipment {
    [BepInDependency(R2API.ContentManagement.R2APIContentManager.PluginGUID)]
    public class SkillCreator {

        public SkillDef CreateSkillFromEquipment(EquipmentDef equipmentDef) {
            GameObject commandoBodyPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Commando/CommandoBody.prefab").WaitForCompletion();

            // Now we must create a SkillDef
            SkillDef mySkillDef = ScriptableObject.CreateInstance<SkillDef>();

            //Check step 2 for the code of the CustomSkillsTutorial.MyEntityStates.SimpleBulletAttack class
            float cooldown;
            if (equipmentDef == RoR2Content.Equipment.GoldGat) {
                cooldown = 0.5f;
            } else {
                cooldown = equipmentDef.cooldown;
            }

            mySkillDef.activationState = new SerializableEntityStateType(typeof(UseEquipmentAttack));
            mySkillDef.activationStateMachineName = "Weapon";
            mySkillDef.baseMaxStock = 1;
            mySkillDef.baseRechargeInterval = cooldown;
            mySkillDef.beginSkillCooldownOnSkillEnd = true;
            mySkillDef.canceledFromSprinting = false;
            mySkillDef.cancelSprintingOnActivation = false;
            mySkillDef.fullRestockOnAssign = false;
            mySkillDef.interruptPriority = InterruptPriority.Any;
            mySkillDef.isCombatSkill = true;
            mySkillDef.mustKeyPress = true;
            mySkillDef.rechargeStock = 1;
            mySkillDef.requiredStock = 1;
            mySkillDef.stockToConsume = 1;
            // For the skill icon, you will have to load a Sprite from your own AssetBundle
            mySkillDef.icon = equipmentDef.pickupIconSprite;
            mySkillDef.skillDescriptionToken = equipmentDef.descriptionToken;
            mySkillDef.skillName = equipmentDef.name;
            mySkillDef.skillNameToken = equipmentDef.nameToken;

            // This adds our skilldef. If you don't do this, the skill will not work.
            try {
                ContentAddition.AddSkillDef(mySkillDef);
            } catch (Exception e) {

            }

            return mySkillDef;
        }
    }

    public class UseEquipmentAttack : BaseSkillState {

        private float duration = 1f;

        //OnEnter() runs once at the start of the skill
        //All we do here is create a BulletAttack and fire it
        public override void OnEnter() {
            base.OnEnter();
            EquipmentIndex equipIndex = EquipmentCatalog.FindEquipmentIndex(base.activatorSkillSlot.skillDef.skillName);
            EquipmentDef equip = EquipmentCatalog.GetEquipmentDef(equipIndex);
            CharacterBody playerBody = base.activatorSkillSlot.characterBody;
            Inventory inventory = playerBody.inventory;
            EquipmentSlot slot = playerBody.equipmentSlot;

            // Fire Equipment
            slot.PerformEquipmentAction(equip);

            if(equipIndex == DLC1Content.Equipment.MultiShopCard.equipmentIndex) {
                return;
            } else if (equipIndex == RoR2Content.Equipment.GoldGat.equipmentIndex) {
                return;
            }

            // Bottled Chaos
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
            Util.ShuffleList(list, slot.rng);
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
                while (!slot.PerformEquipmentAction(EquipmentCatalog.GetEquipmentDef(equipmentIndex)));
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

        //This method runs once at the end
        //Here, we are doing nothing
        public override void OnExit() {
            base.OnExit();
            
            /*PlayerCharacterMasterController.instances[0].master.GetBody().skillLocator.primary.finalRechargeInterval = PlayerCharacterMasterController.instances[0].master.GetBody().inventory.equipmentStateSlots[equipmentIndex].chargeFinishTime.timeUntil;
            PlayerCharacterMasterController.instances[0].master.GetBody().skillLocator.primary.RecalculateValues();*/
        }

        //FixedUpdate() runs almost every frame of the skill
        //Here, we end the skill once it exceeds its intended duration
        public override void FixedUpdate() {
            base.FixedUpdate();
            if (base.fixedAge >= this.duration && base.isAuthority) {
                this.outer.SetNextStateToMain();
                return;
            }
        }

        //GetMinimumInterruptPriority() returns the InterruptPriority required to interrupt this skill
        public override InterruptPriority GetMinimumInterruptPriority() {
            return InterruptPriority.Skill;
        }
    }
}
