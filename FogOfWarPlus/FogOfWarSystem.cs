﻿using System;
using System.Collections.Generic;
using System.Linq;
using Xenko.Core.Mathematics;
using Xenko.Engine;
using Xenko.Rendering;

namespace FogOfWarPlus
{
    public class FogOfWarSystem : SyncScript
    {
        public float FogOpacity;

        private Dictionary<string, FogSubscriber> fogSubscribers;
        private Dictionary<string, FogDetector> fogDetectors;

        private class FogDetector
        {
            internal readonly string Name;
            internal Vector3 Pos
            {
                get
                {
                    detector.Transform.GetWorldTransformation(out var pos, out _, out _);
                    return pos;
                }
            }

            private readonly Entity detector;

            internal FogDetector(Entity entity)
            {
                Name = entity.Name;
                detector = entity;
            }
        }

        private class FogSubscriber
        {
            internal readonly string Name;

            private readonly Entity subscriber;
            private readonly ParameterCollection shaderParams;
            private float closestDetectorDistance;
            private float detectorDistanceRecycler;
            private Vector3 worldPosRecycler;
            private float distanceRecycler;
            private float alphaRecycler;

            private static Vector3 CameraWorldPos = Vector3.Zero;
            private static readonly (bool, Vector3)[] DetectorWorldPos = new (bool, Vector3)[25];
            private const float CameraRange = 25f;
            private const float DetectDistance = 5f;
            private const float DetectFade = 1f;
            private const float DetectZeroThreshold = .01f;

            internal FogSubscriber(Entity entity)
            {
                Name = entity.Name;
                alphaRecycler = 0;
                subscriber = entity;
                shaderParams = entity.Get<ModelComponent>()?
                    .GetMaterial(0)?
                    .Passes[0]?
                    .Parameters;
            }

            internal void UpdateAlpha()
            {
                subscriber.Transform.GetWorldTransformation(out worldPosRecycler, out _, out _);

                closestDetectorDistance =  float.MaxValue;

                if (Vector3.Distance(worldPosRecycler, CameraWorldPos) > CameraRange) {
                    shaderParams?.Set(FogOfWarUnitShaderKeys.Alpha, 0f);
                    return;
                }

                for (var j = 0; j < DetectorWorldPos.Length; j++) {
                    detectorDistanceRecycler = DetectorDistance(j);
                    if (detectorDistanceRecycler < closestDetectorDistance) {
                        closestDetectorDistance = detectorDistanceRecycler;

                        // Shortcut fully visible units (equal to zero)
                        if (Math.Abs(detectorDistanceRecycler - 1) < DetectZeroThreshold) {
                            shaderParams?.Set(FogOfWarUnitShaderKeys.Alpha, 1f);
                            break;
                        }
                    }
                }

                // Avoid unnecessarily updating shader parameters.
                if (Math.Abs(alphaRecycler - closestDetectorDistance) < DetectZeroThreshold) {
                    return;
                }

                alphaRecycler = closestDetectorDistance;
                shaderParams?.Set(FogOfWarUnitShaderKeys.Alpha, closestDetectorDistance);
            }

            internal static void UpdateWorld(Vector3 cameraWorldPos, IEnumerable<Vector3> detectorWorldPos)
            {
                CameraWorldPos = cameraWorldPos;

                // Reset array items to default
                for (var i = 0; i < DetectorWorldPos.Length; i++) {
                    if (detectorWorldPos.Count() <= i) {
                        DetectorWorldPos[i].Item1 = false;
                        DetectorWorldPos[i].Item2 = Vector3.Zero;
                        continue;
                    }

                    DetectorWorldPos[i].Item1 = true;
                    DetectorWorldPos[i].Item2 = detectorWorldPos.ElementAt(i);
                }
            }

            private float DetectorDistance(int playerIndex)
            {
                distanceRecycler = Vector3.Distance(worldPosRecycler, DetectorWorldPos[playerIndex].Item2);
                if (distanceRecycler < DetectDistance) {
                    return 1;
                }

                if (distanceRecycler < DetectDistance + DetectFade) {
                    return (DetectFade - (distanceRecycler - DetectDistance)) / DetectFade;
                }

                return 0;
            }
        }

        public override void Start()
        {
            InitializeFogOfWar();
            RegisterFogOfWar();
        }

        public override void Update()
        {
            UpdateFogOfWarSystem();
        }

        public void AddSubscriber(Entity entity)
        {
            var fogSubscriber = new FogSubscriber(entity);
            if (fogSubscribers.ContainsKey(fogSubscriber.Name)) {
                return;
            }

            fogSubscribers.Add(fogSubscriber.Name, fogSubscriber);
        }

        public void RemoveSubscriber(Entity entity)
        {
            if (fogSubscribers.ContainsKey(entity.Name)) {
                fogSubscribers.Remove(entity.Name);
            }
        }

        public void AddDetector(Entity entity)
        {
            var fogDetector = new FogDetector(entity);
            if (fogDetectors.ContainsKey(fogDetector.Name)) {
                return;
            }

            fogDetectors.Add(fogDetector.Name, fogDetector);
        }

        public void RemoveDetector(Entity entity)
        {
            if (fogDetectors.ContainsKey(entity.Name)) {
                fogDetectors.Remove(entity.Name);
            }
        }

        private void UpdateFogOfWarSystem()
        {
            Entity.Transform.GetWorldTransformation(out var worldPos, out _, out _);
            FogSubscriber.UpdateWorld(worldPos, fogDetectors.Select(a => a.Value.Pos));

            foreach (var fogSubscriber in fogSubscribers.Select(a => a.Value)) {
                fogSubscriber.UpdateAlpha();
            }
        }

        private void InitializeFogOfWar()
        {
            fogDetectors = new Dictionary<string, FogDetector>();
            fogSubscribers = new Dictionary<string, FogSubscriber>();

            var modelComponent = Entity.FindChild("FogOfWar").FindChild("FogOfWarLayer1").Get<ModelComponent>();
            modelComponent.Enabled = true;

            Entity.FindChild("Orthographic").Get<CameraComponent>().Enabled = true;

            var perspective = Entity.FindChild("Perspective").Get<CameraComponent>();
            perspective.Enabled = true;
            perspective.Slot = SceneSystem.GraphicsCompositor.Cameras[0].ToSlotId();

            modelComponent.GetMaterial(0).Passes[0].Parameters.Set(FogOfWarPlusShaderKeys.FogOpacity, FogOpacity);
        }

        private void RegisterFogOfWar()
        {
            Services.AddService(this);
        }
    }
}
