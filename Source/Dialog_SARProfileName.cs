using System;
using UnityEngine;
using Verse;

namespace SizedApparelAutoRefit
{
    // Asks for a profile name before exporting.
    public class Dialog_SARProfileName : Window
    {
        private readonly Action<string> onAccept;
        private string name = "my apparel settings";

        public override Vector2 InitialSize => new Vector2(420f, 160f);

        public Dialog_SARProfileName(Action<string> onAccept)
        {
            this.onAccept = onAccept;
            doCloseX = true;
            absorbInputAroundWindow = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0f, 0f, inRect.width, 28f), "Profile name");
            name = Widgets.TextField(new Rect(0f, 30f, inRect.width, 30f), name);

            if (Widgets.ButtonText(new Rect(inRect.width - 120f, inRect.height - 36f, 120f, 32f), "Export"))
            {
                onAccept?.Invoke(name);
                Close();
            }
        }
    }
}
