using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UnityEngine;

namespace Infohazard.Sequencing {
    /// <summary>
    /// Stores all save data as a single JSON string in PlayerPrefs.
    /// </summary>
    /// <remarks>
    /// Behaves like <see cref="FileSaveDataHandler"/>: getting missing data returns defaults without creating
    /// anything, returned objects are copies (changes only persist when passed to a Set method), and data that
    /// can't be serialized is rejected without affecting what is already saved.
    /// </remarks>
    public class PlayerPrefsSaveDataHandler : SaveDataHandler {
        [SerializeField]
        private string _dataPrefName = "SaveData";

        // Mirrors what is saved in PlayerPrefs. Only ever holds data that has serialized successfully.
        private GlobalSaveDataPair _globalData;
        private JsonSerializer _jsonSerializer;

        private JsonSerializer JsonSerializer => _jsonSerializer ??= new JsonSerializer {
            Formatting = Formatting.None,
            ContractResolver = new DefaultContractResolver {
                IgnoreSerializableAttribute = false,
            },
            Converters = {
                new Vector2Converter(),
                new Vector3Converter(),
                new QuaternionConverter(),
            },
        };

        private GlobalSaveDataPair GlobalData => _globalData ??= LoadGlobalDataPair();

        #region Global

        public override GlobalSaveData GetGlobalSaveData(Serializer serializer) {
            return CopyData(GlobalData.Data) ?? new GlobalSaveData();
        }

        public override void SetGlobalSaveData(Serializer serializer, GlobalSaveData globalData) {
            if (!TryCopyForSaving(globalData, out GlobalSaveData copy)) return;
            GlobalData.Data = copy;
            Save();
        }

        public override void ClearGlobalSaveData() {
            _globalData = null;
            PlayerPrefs.DeleteKey(_dataPrefName);
            PlayerPrefs.Save();
        }

        #endregion

        #region Profile

        public override IEnumerable<string> GetAvailableProfiles() {
            return GlobalData.Profiles.Select(p => p.Data.ProfileName).ToList();
        }

        public override ProfileSaveData GetProfileSaveData(Serializer serializer, string profile) {
            return CopyData(FindProfile(profile)?.Data) ?? new ProfileSaveData {
                ProfileName = profile,
            };
        }

        public override void SetProfileSaveData(Serializer serializer, string profile, ProfileSaveData profileData) {
            if (!TryCopyForSaving(profileData, out ProfileSaveData copy)) return;
            GetOrCreateProfile(profile).Data = copy;
            Save();
        }

        public override void ClearProfileSaveData(string profile) {
            if (GlobalData.Profiles.RemoveAll(p => p.Data.ProfileName == profile) == 0) return;
            Save();
        }

        public override void CopyProfileSaveData(Serializer serializer, string sourceProfile, string destProfile) {
            ProfileSaveDataPair source = FindProfile(sourceProfile);
            GlobalData.Profiles.RemoveAll(p => p.Data.ProfileName == destProfile);

            // Copying a profile that doesn't exist leaves the destination empty, like the file handler.
            if (source != null) {
                ProfileSaveDataPair copy = CopyData(source);
                copy.Data.ProfileName = destProfile;
                GlobalData.Profiles.Add(copy);
            }

            Save();
        }

        #endregion

        #region State

        public override IEnumerable<string> GetAvailableStates(string profile) {
            return FindProfile(profile)?.States.Select(s => s.Data.StateName).ToList() ??
                   Enumerable.Empty<string>();
        }

        public override StateSaveData GetStateSaveData(Serializer serializer, string profile, string state) {
            return CopyData(FindState(profile, state)?.Data) ?? new StateSaveData {
                StateName = state,
            };
        }

        public override void SetStateSaveData(Serializer serializer, string profile, string state,
                                              StateSaveData stateData) {
            if (!TryCopyForSaving(stateData, out StateSaveData copy)) return;
            GetOrCreateState(profile, state).Data = copy;
            Save();
        }

        public override void ClearStateSaveData(string profile, string state) {
            ProfileSaveDataPair profilePair = FindProfile(profile);
            if (profilePair == null || profilePair.States.RemoveAll(s => s.Data.StateName == state) == 0) return;
            Save();
        }

        public override void CopyStateSaveData(Serializer serializer, string profile, string sourceState,
                                               string destState) {
            StateSaveDataPair source = FindState(profile, sourceState);
            FindProfile(profile)?.States.RemoveAll(s => s.Data.StateName == destState);

            // Copying a state that doesn't exist leaves the destination empty, like the file handler.
            if (source != null) {
                StateSaveDataPair copy = CopyData(source);
                copy.Data.StateName = destState;
                GetOrCreateProfile(profile).States.Add(copy);
            }

            Save();
        }

        #endregion

        #region Level

        public override IEnumerable<int> GetAvailableLevels(string profile, string state) {
            return FindState(profile, state)?.Levels.Select(l => l.Data.LevelIndex).ToList() ??
                   Enumerable.Empty<int>();
        }

        public override LevelSaveData GetLevelSaveData(Serializer serializer, string profile, string state, int level) {
            return CopyData(FindLevel(profile, state, level)?.Data) ?? new LevelSaveData {
                LevelIndex = level,
            };
        }

        public override void SetLevelSaveData(Serializer serializer, string profile, string state, int level,
                                              LevelSaveData levelData) {
            if (!TryCopyForSaving(levelData, out LevelSaveData copy)) return;
            GetOrCreateLevel(profile, state, level).Data = copy;
            Save();
        }

        public override void ClearLevelSaveData(string profile, string state, int level) {
            StateSaveDataPair statePair = FindState(profile, state);
            if (statePair == null || statePair.Levels.RemoveAll(l => l.Data.LevelIndex == level) == 0) return;
            Save();
        }

        #endregion

        #region Region

        public override IEnumerable<int> GetAvailableRegions(string profile, string state, int level) {
            return FindLevel(profile, state, level)?.Regions.Select(r => r.RegionIndex).ToList() ??
                   Enumerable.Empty<int>();
        }

        public override RegionSaveData GetRegionSaveData(Serializer serializer, string profile, string state, int level,
                                                         int region) {
            RegionSaveData data = FindLevel(profile, state, level)?.Regions
                                                                   .FirstOrDefault(r => r.RegionIndex == region);
            return CopyData(data) ?? new RegionSaveData {
                RegionIndex = region,
            };
        }

        public override void SetRegionSaveData(Serializer serializer, string profile, string state, int level,
                                               int region, RegionSaveData regionData) {
            if (!TryCopyForSaving(regionData, out RegionSaveData copy)) return;
            List<RegionSaveData> regions = GetOrCreateLevel(profile, state, level).Regions;
            int index = regions.FindIndex(r => r.RegionIndex == region);
            if (index < 0) {
                regions.Add(copy);
            } else {
                regions[index] = copy;
            }

            Save();
        }

        public override void ClearRegionSaveData(string profile, string state, int level, int region) {
            LevelSaveDataPair levelPair = FindLevel(profile, state, level);
            if (levelPair == null || levelPair.Regions.RemoveAll(r => r.RegionIndex == region) == 0) return;
            Save();
        }

        #endregion

        #region Private Methods

        private T CopyData<T>(T data) where T : class {
            if (data == null) return null;

            using StringWriter stringWriter = new();
            JsonSerializer.Serialize(stringWriter, data);

            using StringReader stringReader = new(stringWriter.ToString());
            using JsonTextReader jsonReader = new(stringReader);
            return JsonSerializer.Deserialize<T>(jsonReader);
        }

        /// <summary>
        /// Copy data passed to a Set method, so later changes by the caller aren't saved, and data that can't be
        /// serialized is rejected before it reaches the stored data.
        /// </summary>
        private bool TryCopyForSaving<T>(T data, out T copy) where T : class {
            try {
                copy = CopyData(data);
                return true;
            } catch (Exception ex) {
                Debug.LogException(ex);
                copy = null;
                return false;
            }
        }

        private void Save() {
            using StringWriter stringWriter = new();
            JsonSerializer.Serialize(stringWriter, GlobalData);
            PlayerPrefs.SetString(_dataPrefName, stringWriter.ToString());
            PlayerPrefs.Save();
        }

        private GlobalSaveDataPair LoadGlobalDataPair() {
            string json = PlayerPrefs.GetString(_dataPrefName, null);
            GlobalSaveDataPair pair = null;
            if (!string.IsNullOrEmpty(json)) {
                try {
                    using StringReader stringReader = new(json);
                    using JsonTextReader jsonReader = new(stringReader);
                    pair = JsonSerializer.Deserialize<GlobalSaveDataPair>(jsonReader);
                } catch (Exception ex) {
                    Debug.LogException(ex);
                }
            }

            pair ??= new GlobalSaveDataPair();
            pair.Data ??= new GlobalSaveData();
            pair.Profiles ??= new List<ProfileSaveDataPair>();
            return pair;
        }

        private ProfileSaveDataPair FindProfile(string profile) {
            return GlobalData.Profiles.FirstOrDefault(p => p.Data.ProfileName == profile);
        }

        private StateSaveDataPair FindState(string profile, string state) {
            return FindProfile(profile)?.States.FirstOrDefault(s => s.Data.StateName == state);
        }

        private LevelSaveDataPair FindLevel(string profile, string state, int level) {
            return FindState(profile, state)?.Levels.FirstOrDefault(l => l.Data.LevelIndex == level);
        }

        private ProfileSaveDataPair GetOrCreateProfile(string profile) {
            ProfileSaveDataPair profileData = FindProfile(profile);
            if (profileData != null) return profileData;

            profileData = new ProfileSaveDataPair {
                Data = new ProfileSaveData {
                    ProfileName = profile,
                },
                States = new List<StateSaveDataPair>(),
            };
            GlobalData.Profiles.Add(profileData);
            return profileData;
        }

        private StateSaveDataPair GetOrCreateState(string profile, string state) {
            ProfileSaveDataPair profileData = GetOrCreateProfile(profile);
            StateSaveDataPair stateData = profileData.States.FirstOrDefault(s => s.Data.StateName == state);
            if (stateData != null) return stateData;

            stateData = new StateSaveDataPair {
                Data = new StateSaveData {
                    StateName = state,
                },
                Levels = new List<LevelSaveDataPair>(),
            };
            profileData.States.Add(stateData);
            return stateData;
        }

        private LevelSaveDataPair GetOrCreateLevel(string profile, string state, int level) {
            StateSaveDataPair stateData = GetOrCreateState(profile, state);
            LevelSaveDataPair levelData = stateData.Levels.FirstOrDefault(l => l.Data.LevelIndex == level);
            if (levelData != null) return levelData;

            levelData = new LevelSaveDataPair {
                Data = new LevelSaveData {
                    LevelIndex = level,
                },
                Regions = new List<RegionSaveData>(),
            };
            stateData.Levels.Add(levelData);
            return levelData;
        }

        private class GlobalSaveDataPair {
            public GlobalSaveData Data { get; set; }
            public List<ProfileSaveDataPair> Profiles { get; set; }
        }

        private class ProfileSaveDataPair {
            public ProfileSaveData Data { get; set; }
            public List<StateSaveDataPair> States { get; set; }
        }

        private class StateSaveDataPair {
            public StateSaveData Data { get; set; }
            public List<LevelSaveDataPair> Levels { get; set; }
        }

        private class LevelSaveDataPair {
            public LevelSaveData Data { get; set; }
            public List<RegionSaveData> Regions { get; set; }
        }

        #endregion
    }
}
