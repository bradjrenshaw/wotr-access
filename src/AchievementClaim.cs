using System.Collections.Generic;
using Kingmaker;
using Kingmaker.Achievements;
using Kingmaker.Achievements.Blueprints;
using Kingmaker.AreaLogic.Etudes;
using Kingmaker.Blueprints;

namespace WrathAccess
{
    /// <summary>
    /// The Enhancements "claim earned achievements" button. The game awards its story achievements
    /// from ETUDES: each has a play trigger whose only action is ActionAchievementUnlock, fired once
    /// when the etude starts (chapter 2 beginning → "Burning City", and so on). With mods active the
    /// achievement gate refused that one shot, so a tester who played before the gate enhancement
    /// existed (or with it off) has earned achievements the game recorded but never granted. This
    /// re-issues the game's own unlock call for every achievement whose award etude has already
    /// played in the current save — exactly what the game would have done, nothing more. The etude→
    /// achievement pairs are the game's (patch-frozen) data, read from the etudes' own actions.
    /// </summary>
    internal static class AchievementClaim
    {
        // etude guid : achievement guid (World/Etudes/Common/ImportantStates/GameAchievements/*)
        private static readonly string[] Pairs =
        {
            "67d3321ed01a4e58a9ed3e13f94f1d04:fca67c4e38be427fb8b13c37d3cc0b40", // 01_DevouredByDarkness
            "bda5c655ec7b4c188e5f945e05995708:98d01a15c9d049bfbcb50a8111cc2bac", // 02_BurningCity
            "c571a8f741be43f8b25c83dd6996c676:4e16864351434b468be9e4aa22a5c807", // 03_BannerOverTheCitadel
            "9ff3c25c12d94cfda6892d8c22c76d8c:ebccb71dff604cbb83094206be5db73e", // 04_FifthCrusade
            "50e452e3574f4226a49739562f4e414a:3de29f72ff0e4583a644bcc51eccc514", // 05_EmbraceOfTheAbyss
            "f8ad956cff734f319c90c738456d7af2:5dd2621f8a12497e9f89350a6e6818e9", // 06_HeartOfTheFallenLand
            "5895e7d2bc96473c88aa432b70be80e2:e31a3528d9fe4b358d0a60f56fe08b99", // 08_PathOfAngel
            "5fbee005e0a44d23be8f2de81e499302:8368cfdd88ca41d6b78235f92bfe219c", // 09_PathOfDemon
            "0fab8fc657ac4c0ba822d28e68f2b543:b77812e8a1374b099c00cfe46de9c022", // 10_PathOfAzata
            "bcbc8c910fe445cbaffe4b4df2f466ca:b02963770b9c47308599a7d0b8124ed8", // 11_PathOfAeon
            "c4297282ab2e4e628461b42796f1db56:571c23633fce4ba991f180b8effac820", // 12_PathOfTrickster
            "41bf51ce2251476a9e1899b12161f04e:07fa37e3c2074c428332e6e72f230c94", // 13_PathOfLich
            "afa5382c836546c8b2b7899774e6a835:cfc1a632908c4fcd8e200b7d2da32141", // 14_PathOfSwarm
            "a6ca57ac8ac54882bd72ed11078a9f53:1cfc9a9473ea435c9919b697edffacfb", // 15_PathOfDragon
            "e98071963e1f43f8b5c8e5ff39d2141c:1b757afa98b445a18b479cbcad1fceff", // 16_PathOfLegend
            "8303bc57c59d4baa9aa18f360d9a6766:648a1989925e4a9798e93570b908da83", // 17_Transformation
            "221866ff85db4d29b34e813c9ab3bdba:c24d87815b9d46b39d14d78105dc1707", // 18_MythicChoices
            "008bd552aab442d7a45418d0e1764d61:97b6eb75310f4b92a1bc6226e3a93912", // 24_Legacy
            "9715c3f3a1de4f4692d2d03ee816b41d:70895943a56f427b963a22c5aae18d38", // 27_QueensGratitude
            "3a5985f8d7ce4ca9a2a6cb8b397cbc07:9e8dbcbd35e949ee95fdd80841901998", // 28_GreatUpstart
            "c1c32622d1354d48a5c26fd12249f765:a5bdef7d91b04cdc9b800021967a22c8", // 29_StrategicVictory
            "a43192eddffb455db6004059811ac92d:4c735ee6f366474faf190a79c7bf07d4", // 30_HeroicVictory
            "eefbf4071da144349be1c139fd5f7337:9b3cd63919ee4cf7ae87d6473af5ff09", // 32_MythicReinforcements
            "a4d62dd01f1a4ec9adf718bdd77e8f2f:67434d23d4264f53ad873d8a5b74dcc9", // 33_DefendersHeart
            "380213cbd07d4be5967f2a93e11e88d2:1ca302daedab40a48171621b2f88ff52", // 35_AbyssalConspiracy
            "699d898926f64207b83908ffceea5f0a:7c53f2f988564bbda5dcf4b3847a9705", // 36_CoreOfTheRiddle
            "c6832a4a6d8c4343bc231fac14e1c92d:a5fdc64d83ae420099ed7bc94ea796f8", // 45_VoiceFromDreams
            "0fcc9682a3494c51b092101f8a70fbc3:43acad72218f4243aefc79cb7cbcd426", // 46_AllPathsOpen
            "c515b7db83ad4d2b9dcd3a09487ff0eb:b9b2ed51985b4da98b4f73734b8972f4", // 47_MemoriesFrozenInTime
            "b9c6e3e0d6ac42ef8b9a2bad0c36420d:dbe22500ae564d97923a39eb699688a9", // 48_Spark
            "be788247aaa749ae86a824da02adfa48:dbe22500ae564d97923a39eb699688a9", // 48_Spark (convert)
            "c95ca64f070f42748f097cc3ff21d505:c70e1746ba3f4372bca194912833516a", // 49_Flame
            "fb58df5d41a64d8c98f82760478f3953:c2a24e1b066e48d8922761de365f3278", // 50_HeyIKnowYou
            "348bafe679f7472288e0eb34f8cdfc20:f20970633afd48cf88e238768f12436b", // 51_DemonicAndDivine
            "e7af049e671742258d653aa0244fb6e5:228ed7059a4e4d20967abd852df87c85", // 52_TrulyImportantDeed
            "8f3599ab8cdd4739abc96af3a8a92411:3f207ed7f387453ab29335b7183f4909", // 53_TrulyProfaneGift
            "a52b565b5d5142e7b7a8b8ea7d89f1d3:a49e49af3c0949d8882efb2fa756dd6b", // 54_DeathByColumn
            "dbf194fba4dd4ca6b8bb6f6ef00b55c4:4ed338d976814770a1a4e29d89eb7566", // 55_NextDoorPlay
            "dc4371dc741e4337a6307950372abcb7:e884f46f6564461ba3150eba8be6c115", // 56_GetToad
            "70d42d213a2343f78d34e6556c0081ca:5932eb0083584ab6babbbe38ce15fe53", // 57_PathOfDevil
            "f0d1bb0c4788477cabfe7fba61564532:b76ff8936a89423499109b50478d84ec", // 58_MidnightAim
            "b2cb7d5215f7406ea36646b2ff104c6f:ffeaaaba937c47789500237303eaf8fd", // 59_SoMuchOfMe
            "bf2f03321974495bbbcfc89781895757:8e7c48361e25435fb57d0b4e6bc4ed0c", // 60_Ascension
            "6bf40c1799084d23aaada912da390ae4:2e42a1cd421649f3af120ffa9f9debb9", // 61_GrainOfSand
            "37784f847d874397905411f79e6a8f2a:278616a1c5e04d83a8e61e892dfdddc0", // 62_StoryWorthMillenia
            "aa3c036cb70b43deb1d0d6351bfcb980:113c2f36a21c41fc9f614561c19d1101", // 63_SecretOfSecrets
            "c6931b44130349f39e6d7f17d3f6fc6a:20e0957cd42841b4a0e978e802664f9f", // 64_Radiance
            "3ae84d039cd44b10b31226dac7e934fc:24819bcb892c43e9a0a82a390f3e2497", // 65_SubtleHints
        };

        private static readonly System.Reflection.FieldInfo AchievementsField =
            HarmonyLib.AccessTools.Field(typeof(AchievementsManager), "m_Achievements");

        /// <summary>Run the sweep and speak the outcome.</summary>
        public static void Claim()
        {
            var player = Game.Instance?.Player;
            var manager = player?.Achievements;
            var etudes = player?.EtudesSystem;
            if (manager == null || etudes == null)
            {
                Tts.Speak(Loc.T("achievements.not_in_game"), interrupt: true);
                return;
            }
            if (!Enhancements.AchievementsWithMods)
            {
                Tts.Speak(Loc.T("achievements.toggle_off"), interrupt: true);
                return;
            }
            var entities = AchievementsField?.GetValue(manager) as IEnumerable<AchievementEntity>;
            if (entities == null)
            {
                Tts.Speak(Loc.T("achievements.not_in_game"), interrupt: true);
                return;
            }
            var list = new List<AchievementEntity>(entities);
            int claimed = 0, blocked = 0;
            foreach (var pair in Pairs)
            {
                var parts = pair.Split(':');
                var etude = ResourcesLibrary.TryGetBlueprint<BlueprintEtude>(parts[0]);
                var data = ResourcesLibrary.TryGetBlueprint<AchievementData>(parts[1]);
                if (etude == null || data == null) continue;
                bool earned;
                try
                {
                    var fact = etudes.Etudes.GetFact(etude);
                    earned = (fact != null && (fact.IsPlaying || fact.IsCompleted)) || etudes.EtudeIsCompleted(etude);
                }
                catch { continue; }
                if (!earned) continue;
                AchievementEntity entity = null;
                for (int i = 0; i < list.Count; i++) if (list[i].Data == data) { entity = list[i]; break; }
                if (entity == null || entity.IsUnlocked) continue;
                if (entity.IsDisabled) { blocked++; continue; } // another rule (difficulty, ironman, ...)
                manager.Unlock(data); // the game's own call — the etude action's one-liner
                if (entity.IsUnlocked) claimed++;
            }
            Main.Log?.Log("[achievements] claim sweep: " + claimed + " unlocked, " + blocked + " blocked by other rules");
            if (claimed > 0) Tts.Speak(Loc.T("achievements.claimed", new { count = claimed }), interrupt: true);
            else Tts.Speak(Loc.T("achievements.none"), interrupt: true);
        }
    }
}
