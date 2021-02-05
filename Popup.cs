using System;

namespace SCPI
{
    internal enum PopupType
    {
        Yesno,
        Yesonly
    }

    internal class MyPopup
    {
        public readonly PopupType Type;
        public readonly string Title, Body, OkText, CancelText;
        public readonly Action OkCallback, CancelCallback;

        internal MyPopup(PopupType type = PopupType.Yesonly, string body = "", string title = "",
            string okText = "OK", string cancelText = "cancel", Action okCallback = null, Action cancelCallback = null)
        {
            Type = type;
            Title = title;
            Body = body;
            OkText = okText;
            CancelText = cancelText;
            OkCallback = okCallback ?? (() => { });
            CancelCallback = cancelCallback ?? (() => { });
        }
    }
}