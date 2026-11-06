using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NearbyChests
{
    /// <summary>
    /// Invisible strip across the top of the Nearby chests panel. Drag it to move the panel; right-click it to put
    /// the panel back in its default spot. The position is stored as PanelOffsetX / PanelOffsetY in the config.
    /// </summary>
    internal sealed class PanelDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        private const float Limit = 4000f;

        /// <summary>Offset used while dragging (written to the config when the drag ends).</summary>
        public static Vector2? Live;

        public static Vector2 Offset => Live ?? new Vector2(Plugin.PanelOffsetX.Value, Plugin.PanelOffsetY.Value);

        private RectTransform _space;   // the panel's parent: offsets are in its local units
        private Vector2 _startOffset, _startPoint;

        public static void Create(RectTransform panel, float height, float rightInset)
        {
            var go = new GameObject("NearbyChests_DragHandle", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(0f, -height);
            rt.offsetMax = new Vector2(-rightInset, 0f);
            go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f); // invisible but still catches the mouse
            go.AddComponent<PanelDragHandle>();
        }

        private bool LocalPoint(PointerEventData e, out Vector2 p)
        {
            _space = transform.parent != null ? transform.parent.parent as RectTransform : null;
            p = default;
            return _space != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(_space, e.position, e.pressEventCamera, out p);
        }

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || !LocalPoint(e, out _startPoint)) return;
            _startOffset = Offset;
            Live = _startOffset;
        }

        public void OnDrag(PointerEventData e)
        {
            if (Live == null || !LocalPoint(e, out var p)) return;
            var o = _startOffset + (p - _startPoint);
            Live = new Vector2(Mathf.Clamp(o.x, -Limit, Limit), Mathf.Clamp(o.y, -Limit, Limit));
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (Live == null) return;
            var o = Live.Value;
            Live = null;
            Plugin.PanelOffsetX.Value = Mathf.Round(o.x);
            Plugin.PanelOffsetY.Value = Mathf.Round(o.y);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Right) return;
            Live = null;
            Plugin.PanelOffsetX.Value = 0f;
            Plugin.PanelOffsetY.Value = 0f;
        }
    }
}
