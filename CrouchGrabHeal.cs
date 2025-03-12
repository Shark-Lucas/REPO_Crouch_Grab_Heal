using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using Photon.Pun;

namespace CustomHealthTransfer
{
    [BepInPlugin("SharkLucas.REPO.CrouchGrabHeal", "R.E.P.O. Crouch Grab Heal", "1.0.0")]
    public class CrouchGrabHealPlugin : BaseUnityPlugin
    {
        // 单例实例
        public static CrouchGrabHealPlugin Instance { get; private set; }

        // 配置参数
        public ConfigEntry<float> CustomTransferInterval;
        public ConfigEntry<int> CustomHealAmount;
        public ConfigEntry<int> CustomDamageAmount;
        public ConfigEntry<bool> ShowIndicator;

        private static readonly AccessTools.FieldRef<PlayerHealthGrab, bool> colliderActiveRef = AccessTools.FieldRefAccess<PlayerHealthGrab, bool>("colliderActive");
        private static readonly AccessTools.FieldRef<PlayerHealthGrab, StaticGrabObject> staticGrabObjectRef = AccessTools.FieldRefAccess<PlayerHealthGrab, StaticGrabObject>("staticGrabObject");
        private static readonly AccessTools.FieldRef<PlayerHealthGrab, float> hideLerpRef = AccessTools.FieldRefAccess<PlayerHealthGrab, float>("hideLerp");
        private static readonly AccessTools.FieldRef<PlayerHealthGrab, Collider> physColliderRef = AccessTools.FieldRefAccess<PlayerHealthGrab, Collider>("physCollider");
        private static readonly AccessTools.FieldRef<PlayerHealth, int> healthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("health");
        private static readonly AccessTools.FieldRef<PlayerHealth, int> maxHealthRef = AccessTools.FieldRefAccess<PlayerHealth, int>("maxHealth");

        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> isCrouchingRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("isCrouching");
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> isTumblingRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("isTumbling");
        private static readonly AccessTools.FieldRef<PlayerAvatar, bool> isDisabledRef = AccessTools.FieldRefAccess<PlayerAvatar, bool>("isDisabled");

        void Awake()
        {
            Instance = this;

            // 初始化配置
            CustomTransferInterval = Config.Bind("settings", "interval", 0.5f, "蹲下状态下生命值转移的间隔时间（秒）");
            CustomHealAmount = Config.Bind("settings", "increase", 2, "蹲下状态下每次治疗的生命值");
            CustomDamageAmount = Config.Bind("settings", "decrease", 1, "蹲下状态下每次扣除的生命值");

            // 应用Harmony补丁
            Harmony harmony = new Harmony("REPO.CrouchGrabHeal");
            harmony.PatchAll();

            Logger.LogInfo("CrouchGrabHeal loded!");
        }

        // 处理PlayerHealthGrab.Update方法
        [HarmonyPatch(typeof(PlayerHealthGrab), "Update")]
        public class PlayerHealthGrabUpdatePatch
        {
            // 主要补丁逻辑
            static bool Prefix(PlayerHealthGrab __instance, ref float ___grabbingTimer)
            {
                // 只拦截主机的执行
                if (!PhotonNetwork.IsMasterClient)
                {
                    return true; // 非主机直接执行原始方法
                }

                Instance.Logger.LogInfo("Run");

                /* 以下为原始代码 */
                if (isTumblingRef(__instance.playerAvatar) || SemiFunc.RunIsShop() || SemiFunc.RunIsArena())
                {
                    if (hideLerpRef(__instance) < 1f)
                    {
                        hideLerpRef(__instance) += Time.deltaTime * 5f;
                        hideLerpRef(__instance) = Mathf.Clamp(hideLerpRef(__instance), 0f, 1f);
                        __instance.hideTransform.localScale = new Vector3(1f, __instance.hideCurve.Evaluate(hideLerpRef(__instance)), 1f);
                        if (hideLerpRef(__instance) >= 1f)
                        {
                            __instance.hideTransform.gameObject.SetActive(false);
                        }
                    }
                }
                else if (hideLerpRef(__instance) > 0f)
                {
                    if (!__instance.hideTransform.gameObject.activeSelf)
                    {
                        __instance.hideTransform.gameObject.SetActive(true);
                    }
                    hideLerpRef(__instance) -= Time.deltaTime * 2f;
                    hideLerpRef(__instance) = Mathf.Clamp(hideLerpRef(__instance), 0f, 1f);
                    __instance.hideTransform.localScale = new Vector3(1f, __instance.hideCurve.Evaluate(hideLerpRef(__instance)), 1f);
                }
                bool flag = true;
                if (isDisabledRef(__instance.playerAvatar) || hideLerpRef(__instance) > 0f)
                {
                    flag = false;
                }
                if (colliderActiveRef(__instance) != flag)
                {
                    colliderActiveRef(__instance) = flag;
                    physColliderRef(__instance).enabled = colliderActiveRef(__instance);
                }
                __instance.transform.position = __instance.followTransform.position;
                __instance.transform.rotation = __instance.followTransform.rotation;
                /* 以上为原始代码 */

                // 检查是否有玩家抓取了健康组件
                if (colliderActiveRef(__instance) && (!GameManager.Multiplayer() || PhotonNetwork.IsMasterClient))
                {
                    if (staticGrabObjectRef(__instance).playerGrabbing.Count > 0)
                    {
                        // 增加计时器
                        ___grabbingTimer += Time.deltaTime;

                        foreach (PhysGrabber physGrabber in staticGrabObjectRef(__instance).playerGrabbing)
                        {
                            // 检查抓取者是否处于蹲下状态
                            bool isGrabberCrouching = IsPlayerCrouching(physGrabber.playerAvatar);
                            // 根据蹲下状态确定使用的参数
                            float transferInterval = isGrabberCrouching ? Instance.CustomTransferInterval.Value : 1.0f; // 原始间隔为1秒

                            // 检查是否达到间隔时间
                            if (___grabbingTimer >= transferInterval)
                            {
                                PlayerAvatar grabberAvatar = physGrabber.playerAvatar;
                                // 检查是否符合生命值条件
                                if (healthRef(__instance.playerAvatar.playerHealth) != maxHealthRef(__instance.playerAvatar.playerHealth))
                                {
                                    // 确定治疗和伤害量
                                    int healAmount = isGrabberCrouching ? Instance.CustomHealAmount.Value : 10; // 原始治疗为10点
                                    int damageAmount = isGrabberCrouching ? Instance.CustomDamageAmount.Value : 10; // 原始伤害为10点

                                    // 检查抓取者是否有足够生命值
                                    if (healthRef(grabberAvatar.playerHealth) > damageAmount)
                                    {
                                        // 应用治疗和伤害
                                        __instance.playerAvatar.playerHealth.HealOther(healAmount, true);
                                        grabberAvatar.playerHealth.HurtOther(damageAmount, Vector3.zero, false, -1);
                                        grabberAvatar.HealedOther();

                                        // 重置计时器
                                        ___grabbingTimer = 0f;
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        // 没有抓取者时重置计时器
                        ___grabbingTimer = 0f;
                    }
                }
                return false; // 继续执行原始方法的其他部分（位置更新等）
            }

        }

        // 判断玩家是否处于蹲下状态
        private static bool IsPlayerCrouching(PlayerAvatar avatar)
        {
            // 这里需要根据游戏实际情况判断蹲下状态
            // 可能是通过以下几种方式之一:

            // 1. 直接检查蹲下状态属性（如果游戏有）
            return isCrouchingRef(avatar);
            // return avatar.isCrouching;

            // 2. 检查玩家高度/缩放
            // return avatar.transform.localScale.y < 0.8f;

            // 3. 检查控制器输入
            // return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C);

            // 注意：以上三种方法可能需要根据游戏实际实现进行调整
            // 如果游戏使用专门的蹲下系统，需要找到相应的属性或方法
        }
    }
}