using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Tarinoi.Ui
{
    /// <summary>
    /// Builds the sample interface in code.
    /// </summary>
    /// <remarks>
    /// Deliberately not shipped as prefabs. Prefabs would pull in TextMeshPro's one-time
    /// "import essentials" step, carry a font and theme you would only want to replace,
    /// and merge badly. Built in code, the sample runs in an empty scene with no setup at
    /// all — and the code shows exactly which runtime events drive which widget, which is
    /// the point of a sample.
    /// <para>
    /// It uses the legacy <see cref="Text"/> component for the same reason: it needs no
    /// imported assets. Real games should replace this layer wholesale.
    /// </para>
    /// </remarks>
    public static class QuickstartUi
    {
        public static readonly Color Background = new Color(0.09f, 0.10f, 0.13f, 1f);
        public static readonly Color Speaker = new Color(0.45f, 0.68f, 0.95f, 1f);
        public static readonly Color Body = new Color(0.90f, 0.91f, 0.94f, 1f);
        public static readonly Color SystemLine = new Color(0.92f, 0.78f, 0.35f, 1f);
        public static readonly Color Dimmed = new Color(0.55f, 0.55f, 0.60f, 0.75f);
        public static readonly Color ButtonNormal = new Color(0.17f, 0.19f, 0.24f, 1f);

        static Font _font;

        /// <summary>The built-in font, so the sample needs no imported assets.</summary>
        public static Font Font =>
            _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>Creates a full-screen canvas, adding an event system if the scene lacks one.</summary>
        public static Canvas CreateCanvas(string name, Transform parent = null)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();
            return canvas;
        }

        /// <summary>
        /// Makes sure UI clicks are delivered.
        /// </summary>
        /// <remarks>
        /// The right input module depends on which input backend the project uses, and the
        /// package does not depend on the Input System. The module type is therefore
        /// resolved by name: present means use it, absent means fall back to the legacy
        /// module. This keeps the sample working under either backend without the package
        /// taking on a dependency.
        /// </remarks>
        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));

            var inputSystemModule = Type.GetType(
                "UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");

            if (inputSystemModule != null)
            {
                go.AddComponent(inputSystemModule);
            }
            else
            {
                go.AddComponent<StandaloneInputModule>();
            }
        }

        public static RectTransform CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;

            var rect = go.GetComponent<RectTransform>();
            Stretch(rect);
            return rect;
        }

        public static Text CreateText(Transform parent, string name, string content,
            int size, Color color, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = true;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return text;
        }

        public static Button CreateButton(Transform parent, string label, Action onClick,
            int size = 18)
        {
            var go = new GameObject("Button", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = ButtonNormal;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            var text = CreateText(go.transform, "Label", label, size, Body);
            Stretch(text.rectTransform, 12);
            text.alignment = TextAnchor.MiddleLeft;

            var fitter = go.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var layout = go.AddComponent<LayoutElement>();
            layout.minHeight = 34;

            return button;
        }

        /// <summary>A vertically stacked, scrollable region that grows with its content.</summary>
        public static RectTransform CreateScrollingColumn(Transform parent, out ScrollRect scrollRect,
            int spacing = 10)
        {
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGo.transform.SetParent(parent, false);
            Stretch(scrollGo.GetComponent<RectTransform>());

            scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.movementType = ScrollRectMovementType();
            scrollRect.scrollSensitivity = 25f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            Stretch(viewportGo.GetComponent<RectTransform>());
            viewportGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.01f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;
            scrollRect.viewport = viewportGo.GetComponent<RectTransform>();

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);

            var content = contentGo.GetComponent<RectTransform>();
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = new Vector2(0, 0);
            content.offsetMax = new Vector2(0, 0);

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.content = content;
            return content;
        }

        static ScrollRect.MovementType ScrollRectMovementType() => ScrollRect.MovementType.Clamped;

        public static void Stretch(RectTransform rect, float padding = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>Removes every child, for rebuilding a list.</summary>
        public static void ClearChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }
    }
}
