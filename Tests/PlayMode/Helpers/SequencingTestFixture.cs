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
using System.IO;
using System.Reflection;
using Infohazard.Core;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Infohazard.Sequencing.Tests.PlayMode {
    /// <summary>
    /// Which <see cref="SaveDataHandler"/> a test's <see cref="PersistenceManager"/> uses.
    /// </summary>
    public enum TestHandlerType {
        File,
        PlayerPrefs,
    }

    /// <summary>
    /// Base fixture for Sequencing tests. Gives each test its own save folder and PlayerPrefs keys (so the real
    /// development saves are never touched), builds in-memory level manifests, destroys everything it created after
    /// each test, then fails the test if any persisted object or level registration leaked.
    /// </summary>
    public abstract class SequencingTestFixture {
        private const string PlayerPrefsKeyPrefix = "Infohazard.Sequencing.Tests.";

        private readonly List<Object> _created = new();
        private readonly List<string> _playerPrefsKeys = new();

        /// <summary>
        /// Folder this test's file save data goes in. Deleted after the test.
        /// </summary>
        protected string SaveFolder { get; private set; }

        /// <summary>
        /// The manifest installed by <see cref="CreateManifest"/>, if any.
        /// </summary>
        protected LevelManifest Manifest { get; private set; }

        [SetUp]
        public void SequencingTestFixtureSetUp() {
            // A previous test that leaked has already failed; don't let it affect this one.
            ResetAllStaticState();
            SaveFolder = Path.Combine(Path.GetTempPath(), "InfohazardSequencingTests", Guid.NewGuid().ToString("N"));
        }

        /// <summary>
        /// True for fixtures that must do asynchronous cleanup (such as unloading scenes) before the leak check.
        /// Unity runs [UnityTearDown] after [TearDown], so those fixtures call <see cref="FinishTest"/> themselves
        /// at the end of their [UnityTearDown].
        /// </summary>
        protected virtual bool DefersTearDown => false;

        [TearDown]
        public void SequencingTestFixtureTearDown() {
            if (!DefersTearDown) FinishTest();
        }

        /// <summary>
        /// Destroy everything the test created, delete its save data, then fail if static state leaked.
        /// </summary>
        protected void FinishTest() {
            for (int i = _created.Count - 1; i >= 0; i--) {
                if (_created[i]) Object.DestroyImmediate(_created[i]);
            }

            _created.Clear();

            foreach (string key in _playerPrefsKeys) {
                PlayerPrefs.DeleteKey(key);
            }

            _playerPrefsKeys.Clear();

            if (Directory.Exists(SaveFolder)) Directory.Delete(SaveFolder, true);
            string saveRoot = Path.GetDirectoryName(SaveFolder);
            if (Directory.Exists(saveRoot) && Directory.GetFileSystemEntries(saveRoot).Length == 0) {
                Directory.Delete(saveRoot);
            }

            Manifest = null;
            List<string> leaks = GetStaticStateLeaks();
            ResetAllStaticState();
            Assert.That(leaks, Is.Empty, "Sequencing state leaked after the test's objects were destroyed.");
        }

        #region Static state

        /// <summary>
        /// Describe any persisted objects or level registrations left in Sequencing's static state.
        /// </summary>
        public static List<string> GetStaticStateLeaks() {
            List<string> leaks = new List<string>();
            if (PersistedGameObject.Objects.Count > 0) {
                leaks.Add($"{PersistedGameObject.Objects.Count} PersistedGameObject(s) still registered");
            }

            if (!ReferenceEquals(LevelRoot.Current, null)) leaks.Add("LevelRoot.Current is still set");
            return leaks;
        }

        /// <summary>
        /// Reset all of Sequencing's static state, including its singletons and the level manifest.
        /// </summary>
        public static void ResetAllStaticState() {
            PersistedGameObject.ResetStaticState();
            LevelRoot.ResetStaticState();
            PersistenceManager.ResetStaticState();
            SceneLoadingManager.ResetStaticState();
            SaveStateManager.ResetStaticState();
            SimpleSceneController.ResetStaticState();
            LoadingScreen.ResetStaticState();
            LevelManifest.ResetStaticState();
            Pause.ResetStaticState();
        }

        #endregion

        #region Object helpers

        /// <summary>
        /// Track an object so it is destroyed after the test.
        /// </summary>
        protected T Track<T>(T obj) where T : Object {
            _created.Add(obj);
            return obj;
        }

        /// <summary>
        /// Create a tracked GameObject.
        /// </summary>
        protected GameObject CreateGameObject(string name, bool active = true) {
            GameObject obj = new GameObject(name);
            obj.SetActive(active);
            return Track(obj);
        }

        /// <summary>
        /// Create a tracked ScriptableObject.
        /// </summary>
        protected T CreateAsset<T>(string name = null) where T : ScriptableObject {
            T asset = ScriptableObject.CreateInstance<T>();
            asset.name = name ?? typeof(T).Name;
            return Track(asset);
        }

        /// <summary>
        /// Set a (possibly private, possibly inherited) instance field by name.
        /// </summary>
        public static void SetField(object target, string fieldName, object value) {
            FieldInfo field = FindField(target.GetType(), fieldName);
            field.SetValue(target, value);
        }

        /// <summary>
        /// Get a (possibly private, possibly inherited) instance field by name.
        /// </summary>
        public static T GetField<T>(object target, string fieldName) {
            FieldInfo field = FindField(target.GetType(), fieldName);
            return (T) field.GetValue(target);
        }

        private static FieldInfo FindField(Type type, string fieldName) {
            for (Type t = type; t != null; t = t.BaseType) {
                FieldInfo field = t.GetField(fieldName,
                                             BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                             BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }

            throw new ArgumentException($"Field {fieldName} not found on {type}.");
        }

        /// <summary>
        /// In EditMode, Unity doesn't call Awake on components added by tests. Call a private parameterless
        /// lifecycle method (such as Awake) by name when not playing; in play mode, Unity already called it.
        /// </summary>
        public static void InvokeLifecycleInEditMode(Component component, string methodName = "Awake") {
            if (Application.isPlaying) return;
            for (Type t = component.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType) {
                MethodInfo method = t.GetMethod(methodName,
                                                BindingFlags.Instance | BindingFlags.Public |
                                                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                                                null, Type.EmptyTypes, null);
                if (method == null) continue;
                method.Invoke(component, null);
                return;
            }
        }

        #endregion

        #region Persistence helpers

        /// <summary>
        /// Create a JSON serializer component on the given object.
        /// </summary>
        protected Serializer CreateSerializer(GameObject obj) {
            Serializer serializer = obj.AddComponent<JsonStringSerializer>();

            InvokeLifecycleInEditMode(serializer);
            return serializer;
        }

        /// <summary>
        /// Create a save data handler on the given object that stores data only for this test:
        /// file data under <see cref="SaveFolder"/>, or PlayerPrefs data under a key deleted after the test.
        /// Handlers created with the same <paramref name="storageName"/> share storage, so a second one can be used
        /// to check what actually reached the disk or PlayerPrefs.
        /// </summary>
        protected SaveDataHandler CreateHandler(GameObject obj, TestHandlerType type, string storageName = "Default") {
            switch (type) {
                case TestHandlerType.File: {
                    FileSaveDataHandler handler = obj.AddComponent<FileSaveDataHandler>();
                    // Path.Combine returns the second path when it is absolute, so this replaces persistentDataPath.
                    SetField(handler, "_globalDataFolder", Path.Combine(SaveFolder, storageName));
                    InvokeLifecycleInEditMode(handler);
                    return handler;
                }
                case TestHandlerType.PlayerPrefs: {
                    PlayerPrefsSaveDataHandler handler = obj.AddComponent<PlayerPrefsSaveDataHandler>();
                    string key = PlayerPrefsKeyPrefix + Path.GetFileName(SaveFolder) + "." + storageName;
                    if (!_playerPrefsKeys.Contains(key)) _playerPrefsKeys.Add(key);
                    SetField(handler, "_dataPrefName", key);
                    InvokeLifecycleInEditMode(handler);
                    return handler;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        /// <summary>
        /// Create a PersistenceManager with its own serializer and handler. It becomes
        /// <see cref="Singleton{T}.Instance"/> (any previous instance is forgotten).
        /// </summary>
        protected PersistenceManager CreatePersistenceManager(TestHandlerType handlerType = TestHandlerType.File,
                                                              string storageName = "Default") {
            GameObject obj = CreateGameObject("Test PersistenceManager", false);
            Serializer serializer = CreateSerializer(obj);
            SaveDataHandler handler = CreateHandler(obj, handlerType, storageName);
            PersistenceManager manager = obj.AddComponent<PersistenceManager>();
            SetField(manager, "_serializer", serializer);
            SetField(manager, "_dataHandler", handler);
            obj.SetActive(true);

            PersistenceManager.ResetStaticState();
            Assert.AreSame(manager, PersistenceManager.Instance);
            return manager;
        }

        /// <summary>
        /// Simulate quitting and relaunching: destroy the current PersistenceManager, create a new one reading the
        /// same storage through fresh serializer and handler instances, and load the given global, profile and state
        /// data (pass null to stop at that level).
        /// </summary>
        protected PersistenceManager ReloadPersistenceManager(PersistenceManager current, string profile,
                                                              string state,
                                                              TestHandlerType handlerType = TestHandlerType.File,
                                                              string storageName = "Default") {
            if (current) Object.DestroyImmediate(current.gameObject);
            PersistenceManager manager = CreatePersistenceManager(handlerType, storageName);
            manager.LoadGlobalData();
            if (profile == null) return manager;
            manager.LoadProfileData(profile);
            if (state == null) return manager;
            manager.LoadStateData(state);
            return manager;
        }

        #endregion

        #region Singleton helpers

        /// <summary>
        /// Create a singleton component, run OnEnable (so persisted singletons hear data load events), and make it
        /// <see cref="Singleton{T}.Instance"/>, replacing any previous instance (destroy that one first).
        /// </summary>
        /// <param name="configure">Called before the singleton is enabled and initialized.</param>
        protected T CreateSingleton<T>(Action<T> configure = null) where T : SingletonBase {
            GameObject obj = CreateGameObject(typeof(T).Name, false);
            T singleton = obj.AddComponent<T>();
            configure?.Invoke(singleton);
            obj.SetActive(true);
            InvokeLifecycleInEditMode(singleton, "OnEnable");

            typeof(Singleton<>).MakeGenericType(typeof(T)).GetMethod(nameof(Singleton<T>.ResetStaticState))!
                               .Invoke(null, null);
            T instance = (T) typeof(Singleton<>).MakeGenericType(typeof(T))
                                                .GetProperty(nameof(Singleton<T>.Instance))!.GetValue(null);
            Assert.AreSame(singleton, instance);
            return singleton;
        }

        #endregion

        #region Sequencer helpers

        /// <summary>
        /// Create a step component on its own tracked GameObject.
        /// </summary>
        protected T CreateStep<T>(string name = null) where T : Component {
            return CreateGameObject(name ?? typeof(T).Name).AddComponent<T>();
        }

        /// <summary>
        /// Create a sequencer whose children are the given steps, in order. It doesn't play on awake.
        /// </summary>
        protected ExecutionStepSequencer CreateSequencer(string name, params Component[] steps) {
            GameObject obj = CreateGameObject(name, false);
            foreach (Component step in steps) {
                step.transform.SetParent(obj.transform, false);
            }

            ExecutionStepSequencer sequencer = obj.AddComponent<ExecutionStepSequencer>();
            SetField(sequencer, "_playOnAwake", false);
            obj.SetActive(true);
            InvokeLifecycleInEditMode(sequencer);
            return sequencer;
        }

        /// <summary>
        /// Create a PersistedSceneController whose sequencers are each a single <see cref="RecordingStep"/>,
        /// and make it <see cref="SimpleSceneController.Instance"/>.
        /// </summary>
        protected PersistedSceneController CreateRecordingSceneController(out RecordingStep loadState,
                                                                          out RecordingStep loadProfile,
                                                                          out RecordingStep goToMainMenu) {
            loadState = CreateStep<RecordingStep>("LoadState");
            loadProfile = CreateStep<RecordingStep>("LoadProfile");
            goToMainMenu = CreateStep<RecordingStep>("GoToMainMenu");

            PersistedSceneController controller = CreateGameObject("Scene controller").AddComponent<PersistedSceneController>();
            SetField(controller, "_loadStateSequencer", CreateSequencer("Load state sequencer", loadState));
            SetField(controller, "_loadProfileSequencer", CreateSequencer("Load profile sequencer", loadProfile));
            SetField(controller, "_goToMainMenuSequencer", CreateSequencer("Main menu sequencer", goToMainMenu));

            SimpleSceneController.ResetStaticState();
            Assert.AreSame(controller, PersistedSceneController.Instance);
            return controller;
        }

        #endregion

        #region Manifest helpers

        /// <summary>
        /// Create a region manifest entry.
        /// </summary>
        protected LevelManifestRegionEntry CreateRegionEntry(int regionID, int levelID, string scenePath,
                                                             bool loadedByDefault = false, bool alwaysLoaded = false) {
            LevelManifestRegionEntry region = CreateAsset<LevelManifestRegionEntry>($"Region {levelID}.{regionID}");
            SetField(region, "_regionID", regionID);
            SetField(region, "_levelID", levelID);
            SetField(region, "_scene", CreateSceneRef(scenePath));
            SetField(region, "_loadedByDefault", loadedByDefault);
            SetField(region, "_alwaysLoaded", alwaysLoaded);
            return region;
        }

        /// <summary>
        /// Create a level manifest entry with the given regions.
        /// </summary>
        protected LevelManifestLevelEntry CreateLevelEntry(int levelID, string scenePath,
                                                           params LevelManifestRegionEntry[] regions) {
            LevelManifestLevelEntry level = CreateAsset<LevelManifestLevelEntry>($"Level {levelID}");
            SetField(level, "_levelID", levelID);
            SetField(level, "_scene", CreateSceneRef(scenePath));
            SetField(level, "_regions", regions);
            return level;
        }

        /// <summary>
        /// Create a manifest with the given levels and install it as <see cref="SingletonAsset{T}.Instance"/>.
        /// </summary>
        protected LevelManifest CreateManifest(params LevelManifestLevelEntry[] levels) {
            LevelManifest manifest = CreateAsset<LevelManifest>();
            SetField(manifest, "_levels", levels);
            LevelManifest.SetInstance(manifest);
            Manifest = manifest;
            return manifest;
        }

        /// <summary>
        /// Create a SceneRef for a scene path.
        /// </summary>
        public static SceneRef CreateSceneRef(string scenePath) {
            object sceneRef = new SceneRef();
            SetField(sceneRef, "_path", scenePath);
            return (SceneRef) sceneRef;
        }

        #endregion
    }
}
