using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using Infohazard.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Infohazard.Sequencing {
    public abstract class PersistedGameObjectBase : MonoBehaviour {
        public ObjectSaveData SaveData { get; protected set; }

        public bool Initialized { get; protected set; }

        public bool Initializing { get; protected set; }

        public abstract ulong InstanceID { get; }

        protected List<IPersistedComponent> Components;

        private string _gameObjectName;

        public event Action LoadCompleted;

        protected void OnLoadCompleted() {
            if (!this) return;
            LoadCompleted?.Invoke();
        }

        protected virtual void Awake() {
            if (!Application.isPlaying) return;

            _gameObjectName = gameObject.name;
            Components = new List<IPersistedComponent>();
            GetPersistedComponents(transform, Components);
        }

        private static readonly List<IPersistedComponent> TempComponents = new List<IPersistedComponent>();
        private static void GetPersistedComponents(Transform transform, List<IPersistedComponent> components) {
            TempComponents.Clear();
            transform.GetComponents(TempComponents);
            components.AddRange(TempComponents);

            for (int i = 0; i < transform.childCount; i++) {
                Transform child = transform.GetChild(i);
                if (child.TryGetComponent(out PersistedGameObjectBase _)) continue;
                GetPersistedComponents(child, components);
            }
        }

        protected void InitializeComponents() {
            // Components are keyed by path and type. Further components of the same type at the same path get an
            // index suffix, so they don't share state; the first keeps the plain key so existing saves still load.
            // Local, because a component's LoadState can spawn and initialize another persisted object.
            Dictionary<string, int> idCounts = new Dictionary<string, int>(Components.Count);
            foreach (IPersistedComponent component in Components) {
                string id = new ComponentID(transform, (Component) component).ToString();
                idCounts.TryGetValue(id, out int count);
                idCounts[id] = count + 1;
                if (count > 0) id = $"{id}#{count}";

                component.Initialize(this, SaveData, id);
            }
        }

        protected async UniTask PostLoad() {
            await UniTask.WhenAll(Components.Select(component => component.PostLoad()));

            Initialized = true;
            Initializing = false;
            OnLoadCompleted();
        }

        public void WriteState() {
            foreach (IPersistedComponent component in Components) {
                if (!(Object) component) {
                    Debug.LogException(new Exception($"GameObject: {_gameObjectName} has a destroyed component."), this);
                    continue;
                }

                component.WriteState();
            }
        }
    }
}
