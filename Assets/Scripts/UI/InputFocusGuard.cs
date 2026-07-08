using TMPro;
using UnityEngine.EventSystems;

namespace AldenFall.UI
{
    public static class InputFocusGuard
    {
        public static bool IsTyping
        {
            get
            {
                var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                if (selected == null)
                    return false;

                var input = selected.GetComponent<TMP_InputField>();
                return input != null && input.isFocused;
            }
        }
    }
}
