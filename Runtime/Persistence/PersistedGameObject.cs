// The MIT License (MIT)
//
// Copyright (c) 2026-present Val Miller
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Infohazard.Core;
using Infohazard.Core.Addressables;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;
#endif

namespace Infohazard.Sequencing {
    [ExecuteAlways]
    public class PersistedGameObject : PersistedGameObjectBase, IPersistedInstance {
        [SerializeField, SelfAssetReference] private AssetReferenceGameObject _prefabReference;
        [SerializeField] private ulong _instanceID;
        [SerializeField] private bool _immediatelyConvert = false;

        private bool _hasCheckedId;

        public PersistedLevelRoot Level {
            get => _level;
            private set {
                if (ReferenceEquals(_level, value)) return;

                if (!ReferenceEquals(_level, null))
                    _level.WillSave -= LevelRoot_WillSave;

                _level = value;

                if (!ReferenceEquals(_level, null))
                    _level.WillSave += LevelRoot_WillSave;
            }
        }

        public PersistedRegionRoot Region {
            get => _region;
            private set {
                if (ReferenceEquals(_region, value)) return;

                if (!ReferenceEquals(_region, null))
                    _region.Unloading -= RegionRoot_Unloading;

                _region = value;

                if (!ReferenceEquals(_region, null))
                    _region.Unloading += RegionRoot_Unloading;
            }
        }

        public AssetReferenceGameObject PrefabReference => _prefabReference;

        public bool IsDynamicInstance { get; private set; }

        public override ulong InstanceID => _instanceID;

        private bool _needsToInitialize = false;
        private PersistedRegionRoot _region;
        private PersistedLevelRoot _level;

        private static Dictionary<ulong, PersistedGameObject> _objects = new Dictionary<ulong, PersistedGameObject>();
        public static IReadOnlyDictionary<ulong, PersistedGameObject> Objects => _objects;

        /// <summary>
        /// Reset all static state of PersistedGameObject to its initial values.
        /// </summary>
        /// <remarks>
        /// This exists for unit tests and editor tooling (such as entering play mode without a domain reload).
        /// It typically should not be called from game code.
        /// </remarks>
        public static void ResetStaticState() {
            _objects.Clear();
            _transforms.Clear();
            _rootGameObjects.Clear();
        }

        protected override void Awake() {
            if (!Application.isPlaying) return;
            base.Awake();
            OnSpawned();
        }

        private void OnDestroy() {
            OnDespawned();
        }

        private PersistedObjectCollection Container {
            get {
                if (Region) return Region.SaveData.Objects;
                if (Level) return Level.SaveData.Objects;
                return null;
            }
        }

        private void OnSpawned() {
            if (!Application.isPlaying) return;
            _needsToInitialize = true;
            IsDynamicInstance = false;
        }

        private void OnDespawned() {
            if (!Application.isPlaying) return;
            UnInitialize();
        }

#if UNITY_EDITOR
        private void UpdateUniqueID() {
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(this);
            ulong newID = id.targetObjectId ^ id.targetPrefabId;
            _hasCheckedId = true;
            if (newID == _instanceID) return;
            Undo.RecordObject(this, "Set Unique ID");
            _instanceID = newID;
        }

        private bool IsPrefab() {
            PrefabStage stage = PrefabStageUtility.GetPrefabStage(gameObject);
            PrefabAssetType type = PrefabUtility.GetPrefabAssetType(gameObject);
            PrefabInstanceStatus status = PrefabUtility.GetPrefabInstanceStatus(gameObject);


            return (stage != null && transform.parent == null) ||
                   ((type == PrefabAssetType.Regular || type == PrefabAssetType.Variant) &&
                    status == PrefabInstanceStatus.NotAPrefab);
        }
#endif

#if UNITY_EDITOR
        private void Update() {
            if (!Application.isPlaying) {
                if ((_instanceID == 0 || !_hasCheckedId) && !IsPrefab()) {
                    UpdateUniqueID();
                }
            }
        }
#endif

        public void SetupDynamicInstance(ulong instanceID) {
            if (!_needsToInitialize) {
                Debug.LogError("SetupDynamicInstance can only be called before a spawned object is initialized.", this);
                return;
            }

            _instanceID = instanceID;
            IsDynamicInstance = true;

            PersistedLevelRoot level = PersistedLevelRoot.Current;
            if (level == null || !level.ObjectsLoaded) return;

            SceneLoadingManager.Instance.GetSceneLoadedState(gameObject.scene.name, out _, out RegionRoot region);
            if (region is PersistedRegionRoot { ObjectsLoaded: false }) return;

            Initialize();
            InitializeComponents();
            PostLoad().Forget();
        }

        public void SetInstanceIDEditMode(ulong instanceID) {
            if (DebugUtility.CheckPlaying()) return;
            _instanceID = instanceID;
        }

        private void Initialize() {
            if (!_needsToInitialize || Initialized || Initializing || Level != null) {
                Debug.LogError($"Trying to initialize object {this} multiple times.", this);
                return;
            }
            _needsToInitialize = false;
            Initializing = true;

            Level = PersistedLevelRoot.Current;

            Scene scene = gameObject.scene;
            SceneLoadingManager.Instance.GetSceneLoadedState(scene.name, out _, out RegionRoot region);

            if (region is PersistedRegionRoot pRegion) Region = pRegion;

            PersistedObjectCollection container = Container;
            SaveData = container != null
                ? _instanceID == 0
                    ? container.RegisterNewDynamicObject(_prefabReference.AssetGUID)
                    : container.RegisterExistingObject(_instanceID, IsDynamicInstance)
                : new ObjectSaveData(_instanceID, false, null);
            _instanceID = SaveData.InstanceID;

            if (SaveData.Destroyed) {
                // Not registered: a destroyed copy (for example the static original of an object that moved to
                // another region, which keeps its ID) must not take over the live object's ID.
                Spawnable.Despawn(gameObject);
            } else {
                if (_objects.TryGetValue(_instanceID, out PersistedGameObject existing) && existing &&
                    existing != this) {
                    Debug.LogError($"Object with instanceID already exists, trying to replace with {name}.", this);
                }

                _objects[_instanceID] = this;

                if (!IsDynamicInstance && _immediatelyConvert) {
                    ConvertToDynamicInstance();
                }
            }
        }

        private void UnInitialize() {
            if (!Initialized) return;

            _needsToInitialize = false;
            IsDynamicInstance = false;
            Initialized = false;
            Level = null;
            Region = null;
            RemoveFromRegistry();
        }

        /// <summary>
        /// Remove this object's registry entry, but only if the entry is this object.
        /// </summary>
        private void RemoveFromRegistry() {
            if (_objects.TryGetValue(_instanceID, out PersistedGameObject registered) &&
                ReferenceEquals(registered, this)) {
                _objects.Remove(_instanceID);
            }
        }

        private void ConvertToDynamicInstance() {
            if (IsDynamicInstance) {
                Debug.LogError($"Trying to convert object {name} with ID {_instanceID} to dynamic, but it already is.", this);
                return;
            }

            if (_prefabReference?.RuntimeKeyIsValid() != true) {
                Debug.LogError($"Object {name} is set to convert to a dynamic instance, " +
                               "but it doesn't have a valid prefab reference, so it stays static.", this);
                return;
            }

            PersistedObjectCollection container = Container;
            if (container == null) return;
            SaveData = container.ConvertStaticObjectToDynamic(_instanceID, _prefabReference.AssetGUID);

            RemoveFromRegistry();
            _instanceID = SaveData.InstanceID;
            IsDynamicInstance = true;
            _objects[_instanceID] = this;
        }

        public void RegisterDestroyed() {
            RemoveFromRegistry();
            Container?.RegisterObjectDestroyed(_instanceID);
        }

        public void TransitionToRegion(PersistedRegionRoot newRoot) {
            if (_prefabReference?.RuntimeKeyIsValid() != true) {
                Debug.LogError($"Trying to transition object {name}, which doesn't have a dynamic prefab ID.", this);
                return;
            }

            WriteState();
            PersistedObjectCollection container = Container;
            if (container == null) return;
            container.RegisterObjectDestroyed(_instanceID);
            Region = newRoot;

            // Update container after changing region.
            container = Container;

            ObjectSaveData data = container.RegisterNewDynamicObject(_prefabReference.AssetGUID, _instanceID);
            data.CustomData = SaveData.CustomData;
            data.Destroyed = false;
            data.Initialized = true;

            SaveData = data;
            IsDynamicInstance = true;
            if (SaveData.InstanceID != _instanceID) {
                RemoveFromRegistry();
                _instanceID = SaveData.InstanceID;
                _objects[_instanceID] = this;
            }

            InitializeComponents();
        }

        private void LevelRoot_WillSave() {
            WriteState();
        }

        private void RegionRoot_Unloading() {
            WriteState();
        }

        public static bool TryGetObjectWithID(ulong id, out PersistedGameObject obj) {
            return _objects.TryGetValue(id, out obj);
        }

        public static PersistedGameObject GetObjectWithID(ulong id) {
            return _objects.TryGetValue(id, out PersistedGameObject obj) ? obj : null;
        }

        public static IEnumerable<PersistedGameObject> GetObjectsWithIDs(IEnumerable<ulong> ids) {
            return ids?.Select(GetObjectWithID).Where(obj => obj) ?? Enumerable.Empty<PersistedGameObject>();
        }

        public static UniTask LoadDynamicObjects(PersistedObjectCollection data, Scene scene, Transform parent) {
            return UniTask.WhenAll(data.Objects.SelectWhere((ObjectSaveData objectData, out UniTask task) => {
                if (!objectData.IsDynamicInstance) {
                    task = UniTask.CompletedTask;
                    return false;
                }

                task = LoadDynamicObjectAsync(objectData, scene, parent);
                return true;
            }));
        }

        private static async UniTask LoadDynamicObjectAsync(ObjectSaveData objectData, Scene scene, Transform parent) {
            // One bad entry (for example a prefab removed from Addressables since the save was made) must not stop
            // the rest of the level from loading. The entry is kept, so it isn't lost if the prefab comes back.
            try {
                PersistedGameObject spawned = await AddressableUtil.SpawnAddressableAsync<PersistedGameObject>(
                    objectData.DynamicPrefabID, new SpawnParams {
                        Parent = parent,
                        Scene = scene,
                        PersistedInstanceID = objectData.InstanceID,
                    });

                if (!spawned) {
                    Debug.LogError($"Failed to spawn saved dynamic object {objectData.InstanceID} " +
                                   $"from prefab {objectData.DynamicPrefabID}.");
                }
            } catch (Exception ex) {
                Debug.LogError($"Failed to spawn saved dynamic object {objectData.InstanceID} " +
                               $"from prefab {objectData.DynamicPrefabID}: {ex}");
            }
        }

        public static UniTask InitializeGameObjects(List<PersistedGameObject> list) {
            foreach (PersistedGameObject obj in list) {
                obj.Initialize();
            }

            foreach (PersistedGameObject obj in list) {
                obj.InitializeComponents();
            }

            return UniTask.WhenAll(list.Select(obj => obj.PostLoad()));
        }

        private static Stack<Transform> _transforms = new Stack<Transform>();
        private static List<GameObject> _rootGameObjects = new List<GameObject>();

        public static void CollectGameObjects(Scene scene, List<PersistedGameObject> list) {
            _transforms.Clear();
            _rootGameObjects.Clear();
            scene.GetRootGameObjects(_rootGameObjects);
            foreach (GameObject rootGameObject in _rootGameObjects) {
                _transforms.Push(rootGameObject.transform);
            }

            while (_transforms.Count > 0) {
                Transform current = _transforms.Pop();
                if (current.gameObject.activeSelf == false) continue;
                if (current.TryGetComponent(out PersistedGameObject obj)) {
                    list.Add(obj);
                    continue; // We don't support nested PersistedGameObjects.
                }

                for (int i = 0; i < current.childCount; i++) {
                    _transforms.Push(current.GetChild(i));
                }
            }
        }
    }
}
