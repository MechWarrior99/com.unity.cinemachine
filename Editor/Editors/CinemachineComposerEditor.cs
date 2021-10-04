using UnityEngine;
using UnityEditor;
using Cinemachine.Utility;

namespace Cinemachine.Editor
{
    [CustomEditor(typeof(CinemachineComposer))]
    [CanEditMultipleObjects]
    internal class CinemachineComposerEditor : BaseEditor<CinemachineComposer>
    {
        CinemachineScreenComposerGuides m_ScreenGuideEditor;
        GameViewEventCatcher m_GameViewEventCatcher;

        protected virtual void OnEnable()
        {
            m_ScreenGuideEditor = new CinemachineScreenComposerGuides();
            m_ScreenGuideEditor.GetHardGuide = () => { return Target.HardGuideRect; };
            m_ScreenGuideEditor.GetSoftGuide = () => { return Target.SoftGuideRect; };
            m_ScreenGuideEditor.SetHardGuide = (Rect r) => { Target.HardGuideRect = r; };
            m_ScreenGuideEditor.SetSoftGuide = (Rect r) => { Target.SoftGuideRect = r; };
            m_ScreenGuideEditor.Target = () => { return serializedObject; };

            m_GameViewEventCatcher = new GameViewEventCatcher();
            m_GameViewEventCatcher.OnEnable();

            CinemachineDebug.OnGUIHandlers -= OnGUI;
            CinemachineDebug.OnGUIHandlers += OnGUI;
            if (CinemachineSettings.CinemachineCoreSettings.ShowInGameGuides)
                InspectorUtility.RepaintGameView();
            
            CinemachineSceneToolUtility.RegisterTool(typeof(TrackedObjectOffsetTool));
        }

        protected virtual void OnDisable()
        {
            m_GameViewEventCatcher.OnDisable();
            CinemachineDebug.OnGUIHandlers -= OnGUI;
            if (CinemachineSettings.CinemachineCoreSettings.ShowInGameGuides)
                InspectorUtility.RepaintGameView();
            
            CinemachineSceneToolUtility.UnregisterTool(typeof(TrackedObjectOffsetTool));
        }

        public override void OnInspectorGUI()
        {
            BeginInspector();
            bool needWarning = false;
            for (int i = 0; !needWarning && i < targets.Length; ++i)
                needWarning = (targets[i] as CinemachineComposer).LookAtTarget == null;
            if (needWarning)
                EditorGUILayout.HelpBox(
                    "A LookAt target is required.  Change Aim to Do Nothing if you don't want a LookAt target.",
                    MessageType.Warning);

            // First snapshot some settings
            Rect oldHard = Target.HardGuideRect;
            Rect oldSoft = Target.SoftGuideRect;

            // Draw the properties
            DrawRemainingPropertiesInInspector();
            m_ScreenGuideEditor.SetNewBounds(oldHard, oldSoft, Target.HardGuideRect, Target.SoftGuideRect);
        }

        protected virtual void OnGUI()
        {
            // Draw the camera guides
            if (Target == null || !CinemachineSettings.CinemachineCoreSettings.ShowInGameGuides)
                return;

            // If inspector is collapsed in the vcam editor, don't draw the guides
            if (!VcamStageEditor.ActiveEditorRegistry.IsActiveEditor(this))
                return;

            // Don't draw the guides if rendering to texture
            var vcam = Target.VirtualCamera;
            CinemachineBrain brain = CinemachineCore.Instance.FindPotentialTargetBrain(vcam);
            if (brain == null || (brain.OutputCamera.activeTexture != null && CinemachineCore.Instance.BrainCount > 1))
                return;

            // Screen guides
            bool isLive = targets.Length <= 1 && brain.IsLive(vcam, true);
            m_ScreenGuideEditor.OnGUI_DrawGuides(isLive, brain.OutputCamera, Target.VcamState.Lens, true);

            // Draw an on-screen gizmo for the target
            if (Target.LookAtTarget != null && isLive)
            {
                Vector3 targetScreenPosition = brain.OutputCamera.WorldToScreenPoint(Target.TrackedPoint);
                if (targetScreenPosition.z > 0)
                {
                    targetScreenPosition.y = Screen.height - targetScreenPosition.y;

                    GUI.color = CinemachineSettings.ComposerSettings.TargetColour;
                    Rect r = new Rect(targetScreenPosition, Vector2.zero);
                    float size = (CinemachineSettings.ComposerSettings.TargetSize
                        + CinemachineScreenComposerGuides.kGuideBarWidthPx) / 2;
                    GUI.DrawTexture(r.Inflated(new Vector2(size, size)), Texture2D.whiteTexture);
                    size -= CinemachineScreenComposerGuides.kGuideBarWidthPx;
                    if (size > 0)
                    {
                        Vector4 overlayOpacityScalar
                            = new Vector4(1f, 1f, 1f, CinemachineSettings.ComposerSettings.OverlayOpacity);
                        GUI.color = Color.black * overlayOpacityScalar;
                        GUI.DrawTexture(r.Inflated(new Vector2(size, size)), Texture2D.whiteTexture);
                    }
                }
            }
        }

        protected override void DrawSceneTools()
        {
            var composer = Target;
            if (!composer.IsValid)
            {
                return;
            }

            if (CinemachineSceneToolUtility.IsToolActive(typeof(TrackedObjectOffsetTool)))
            {
                var lookAtTargetPosition = composer.LookAtTargetPosition;
                var lookAtTargetRotation = composer.LookAtTargetRotation;
                var trackedObjectPosition = 
                    lookAtTargetPosition + lookAtTargetRotation * composer.m_TrackedObjectOffset;

                EditorGUI.BeginChangeCheck();
                var tooHandleMinId = GUIUtility.GetControlID(FocusType.Passive);
                var newPos = Handles.PositionHandle(trackedObjectPosition, lookAtTargetRotation);
                var tooHandleMaxId = GUIUtility.GetControlID(FocusType.Passive);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(composer, "Change Tracked Object Offset using handle in Scene View.");
                    
                    // calculate delta and discard imprecision, then update offset
                    var delta = Quaternion.Inverse(lookAtTargetRotation) * (newPos - trackedObjectPosition);
                    delta = new Vector3(
                        Mathf.Abs(delta.x) < UnityVectorExtensions.Epsilon ? 0 : delta.x,
                        Mathf.Abs(delta.y) < UnityVectorExtensions.Epsilon ? 0 : delta.y,
                        Mathf.Abs(delta.z) < UnityVectorExtensions.Epsilon ? 0 : delta.z);
                    composer.m_TrackedObjectOffset += delta;
                    
                    InspectorUtility.RepaintGameView();
                }
                
                var trackedObjectOffsetHandleIsUsedOrHovered = 
                    tooHandleMinId < GUIUtility.hotControl && GUIUtility.hotControl < tooHandleMaxId || 
                    tooHandleMinId < HandleUtility.nearestControl && HandleUtility.nearestControl < tooHandleMaxId;
                if (trackedObjectOffsetHandleIsUsedOrHovered)
                {
                    var labelStyle = new GUIStyle { normal = { textColor = Handles.selectedColor } };
                    Handles.Label(trackedObjectPosition, "Tracked Object Offset " + 
                        composer.m_TrackedObjectOffset.ToString("F1"), labelStyle);
                }
                var originalColor = Handles.color;
                Handles.color = trackedObjectOffsetHandleIsUsedOrHovered ? 
                    Handles.selectedColor : CinemachineSettings.CinemachineCoreSettings.ActiveGizmoColour;
                Handles.DrawDottedLine(lookAtTargetPosition, trackedObjectPosition, 5f);
                Handles.DrawLine(trackedObjectPosition, composer.VcamState.FinalPosition);
                Handles.color = originalColor;
            }
        }

#if false
        // debugging only
        [DrawGizmo(GizmoType.Active | GizmoType.Selected, typeof(CinemachineComposer))]
        static void DrawComposerGizmos(CinemachineComposer target, GizmoType selectionType)
        {
            // Draw lookahead path
            if (target.m_LookaheadTime > 0)
            {
                Color originalGizmoColour = Gizmos.color;
                Gizmos.color = CinemachineSettings.ComposerSettings.TargetColour;

                var p0 = target.m_Predictor.PredictPosition(0);
                int numSteps = 20;
                for (int i = 1; i <= numSteps; ++i)
                {
                    var p1 = target.m_Predictor.PredictPosition(i * target.m_LookaheadTime / numSteps);
                    Gizmos.DrawLine(p0, p1);
                    p0 = p1;
                }
                Gizmos.color = originalGizmoColour;
            }
        }
#endif
    }
}
