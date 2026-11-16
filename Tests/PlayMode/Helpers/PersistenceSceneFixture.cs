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
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Infohazard.Core;
using Infohazard.Core.Addressables;
using Infohazard.Core.Addressables.Tests.PlayMode;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using AddressablesAPI = UnityEngine.AddressableAssets.Addressables;
using Object = UnityEngine.Object;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// Base fixture for tests of persisted objects in the persisted test scenes. Each test starts with a profile and
    /// an empty (new game) state loaded, and can register in-memory addressable prefabs for dynamic objects.
    /// </summary>
    public abstract class PersistenceSceneFixture : SceneTestFixture {
        protected const string Profile = "Profile";

        private ResourceLocationMap _locator;

        protected PersistenceManager Manager { get; set; }

        [SetUp]
        public async Task PersistenceSceneFixtureSetUp() {
            Manager = CreatePersistenceManager();
            Manager.LoadGlobalData();
            Manager.LoadProfileData(Profile);
            Manager.LoadEmptyStateData();

            await AddressablesAPI.InitializeAsync().Task;
            _locator = new ResourceLocationMap("SequencingTestLocator");
            AddressablesAPI.AddResourceLocator(_locator);
        }

        protected override async UniTask CleanUpAfterScenesAsync() {
            // Pooled instances live under the PoolManager, outside the test scenes.
            foreach (PoolManager poolManager in Object.FindObjectsByType<PoolManager>(FindObjectsInactive.Include,
                                                                                      FindObjectsSortMode.None)) {
                Object.DestroyImmediate(poolManager.gameObject);
            }

            PoolManager.ResetStaticState();
            await UniTask.Yield();
            await UniTask.Yield();
            if (_locator != null) AddressablesAPI.RemoveResourceLocator(_locator);
            _locator = null;
        }

        #region Prefabs

        /// <summary>
        /// The provider that serves in-memory prefabs. Addressables picks providers by ID, so an instance registered
        /// by another test assembly is reused.
        /// </summary>
        private static TestPrefabProvider Provider {
            get {
                TestPrefabProvider provider = AddressablesAPI.ResourceManager.ResourceProviders.OfType<TestPrefabProvider>()
                                                             .FirstOrDefault();
                if (provider != null) return provider;
                provider = new TestPrefabProvider();
                AddressablesAPI.ResourceManager.ResourceProviders.Add(provider);
                return provider;
            }
        }

        /// <summary>
        /// Register an in-memory prefab under an addressable key. The prefab is inactive, has a Spawnable and a
        /// PersistedGameObject referencing the key, and is destroyed after the test.
        /// </summary>
        /// <param name="key">Addressable key in GUID format.</param>
        /// <param name="build">Adds the prefab's other components.</param>
        /// <param name="behavior">How loading the prefab behaves.</param>
        protected GameObject RegisterPersistedPrefab(string key, Action<GameObject> build = null,
                                                     TestPrefabProvider.Behavior behavior =
                                                         TestPrefabProvider.Behavior.Succeed) {
            GameObject prefab = CreateGameObject($"Prefab_{key}", false);
            prefab.AddComponent<Spawnable>();
            PersistedGameObject persisted = prefab.AddComponent<PersistedGameObject>();
            SetField(persisted, "_prefabReference", new AssetReferenceGameObject(key));
            build?.Invoke(prefab);

            TestPrefabProvider provider = Provider;
            string internalId = provider.Register(key, prefab, behavior);
            _locator.Add(key, new ResourceLocationBase(key, internalId, provider.ProviderId, typeof(GameObject)));
            return prefab;
        }

        /// <summary>
        /// A prefab like the scene's Mover: PersistedTransform and CanMoveBetweenRegions.
        /// </summary>
        protected GameObject RegisterMoverPrefab(string key = TestScenes.MoverPrefabKey) {
            return RegisterPersistedPrefab(key, prefab => {
                PersistedTransform persistedTransform = prefab.AddComponent<PersistedTransform>();
                SetField(persistedTransform, "_transform", prefab.transform);
                prefab.AddComponent<CanMoveBetweenRegions>();
            });
        }

        /// <summary>
        /// Spawn a persisted prefab into the current level's dynamic object root, the way gameplay code would.
        /// </summary>
        protected async UniTask<PersistedGameObject> SpawnInLevel(string key, Vector3 position) {
            PersistedLevelRoot level = PersistedLevelRoot.Current;
            PersistedGameObject spawned = await AddressableUtil.SpawnAddressableAsync<PersistedGameObject>(
                key, new SpawnParams {
                    Parent = level.DynamicObjectRoot,
                    Scene = level.gameObject.scene,
                    Position = position,
                    InWorldSpace = true,
                });
            Assert.IsNotNull(spawned, $"Spawning {key} failed.");
            return spawned;
        }

        #endregion

        #region Level and save helpers

        /// <summary>
        /// Load the persisted level and the given regions, then load and initialize their objects, as the level
        /// loading sequence does.
        /// </summary>
        protected async UniTask<PersistedLevelRoot> LoadPersistedLevel(params string[] regionScenes) {
            await LoadAndWait(TestScenes.PLevel);
            foreach (string region in regionScenes) {
                await LoadAndWait(region);
            }

            PersistedLevelRoot level = PersistedLevelRoot.Current;
            Assert.IsNotNull(level, "The persisted level didn't become current.");
            await AwaitOrFail(level.LoadObjects(), "loading objects");
            return level;
        }

        /// <summary>
        /// Load a persisted region into the already-loaded level, and wait for its objects to initialize.
        /// </summary>
        protected async UniTask<PersistedRegionRoot> LoadPersistedRegion(string regionScene) {
            await LoadAndWait(regionScene);
            Loader.GetSceneLoadedState(TestScenes.Name(regionScene), out _, out RegionRoot region);
            PersistedRegionRoot persistedRegion = (PersistedRegionRoot) region;
            await WaitUntilOrFail(() => persistedRegion.ObjectsLoaded, $"objects of {regionScene} load");
            return persistedRegion;
        }

        /// <summary>
        /// Load the persisted level, activate inactive root objects of its scene, then load objects.
        /// </summary>
        protected async UniTask<PersistedLevelRoot> LoadPersistedLevelActivating(params string[] objectNames) {
            await LoadAndWait(TestScenes.PLevel);
            PersistedLevelRoot level = PersistedLevelRoot.Current;
            foreach (GameObject root in level.gameObject.scene.GetRootGameObjects()) {
                if (objectNames.Contains(root.name)) root.SetActive(true);
            }

            await AwaitOrFail(level.LoadObjects(), "loading objects");
            return level;
        }

        protected PersistedRegionRoot GetRegion(string regionScene) {
            Loader.GetSceneLoadedState(TestScenes.Name(regionScene), out _, out RegionRoot region);
            return (PersistedRegionRoot) region;
        }

        /// <summary>
        /// Save everything under a slot name, as SaveStateManager.SaveAll does.
        /// </summary>
        protected void SaveAs(string slot) {
            Manager.SaveStateDataAs(slot);
            if (PersistedLevelRoot.Current) PersistedLevelRoot.Current.Save();
            Manager.SaveProfileData();
            Manager.SaveGlobalData();
        }

        /// <summary>
        /// Unload every scene in the default group, as leaving the level does.
        /// </summary>
        protected async UniTask UnloadAll() {
            Loader.UnloadScenes();
            await WaitUntilOrFail(() => !Loader.IsUnloadingAnyScenes, "unloading scenes");
            Assert.IsNull(LevelRoot.Current);
            Assert.That(PersistedGameObject.Objects, Is.Empty, "Persisted objects survived unloading their scenes.");
        }

        /// <summary>
        /// Save, leave the level, relaunch (fresh PersistenceManager reading the slot from disk), and load the level
        /// and regions again.
        /// </summary>
        protected async UniTask<PersistedLevelRoot> SaveAndReload(string slot, params string[] regionScenes) {
            SaveAs(slot);
            await UnloadAll();
            Manager = ReloadPersistenceManager(Manager, Profile, slot);
            return await LoadPersistedLevel(regionScenes);
        }

        /// <summary>
        /// Find a component on an active scene object by name.
        /// </summary>
        protected static T Find<T>(string objectName) where T : Component {
            GameObject obj = GameObject.Find(objectName);
            return obj ? obj.GetComponent<T>() : null;
        }

        /// <summary>
        /// Live persisted objects spawned from the given prefab key.
        /// </summary>
        protected static PersistedGameObject[] DynamicInstancesOf(string key) {
            return PersistedGameObject.Objects.Values
                                      .Where(o => o && o.IsDynamicInstance && o.PrefabReference?.AssetGUID == key)
                                      .ToArray();
        }

        #endregion
    }
}
