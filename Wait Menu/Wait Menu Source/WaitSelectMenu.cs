using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;

namespace WaitMenu;

public sealed class WaitSelectMenu : IClickableMenu
{
    private readonly Action<int> onPickMinutes;
    private readonly List<ClickableComponent> optionButtons = new();
    private readonly List<int> minutes = new();

    private const int CloseButtonId = 9999;

    public WaitSelectMenu(Action<int> onPickMinutes)
    {
        this.onPickMinutes = onPickMinutes;

        for (int value = 30; value <= 240; value += 30)
            this.minutes.Add(value);

        const int sidePadding = 64;
        const int rowHeight = 56;
        const int buttonWidth = 320;

        int menuWidth = buttonWidth + sidePadding;
        int menuHeight = this.minutes.Count * rowHeight + 128;
        int x = (Game1.viewport.Width - menuWidth) / 2;
        int y = (Game1.viewport.Height - menuHeight) / 2;

        this.xPositionOnScreen = x;
        this.yPositionOnScreen = y;
        this.width = menuWidth;
        this.height = menuHeight;

        this.upperRightCloseButton = new ClickableTextureComponent(
            new Rectangle(x + this.width - 48, y + 16, 32, 32),
            Game1.mouseCursors,
            new Rectangle(337, 494, 12, 12),
            4f)
        {
            myID = CloseButtonId
        };

        int firstRowY = y + 80;
        for (int index = 0; index < this.minutes.Count; index++)
        {
            int rowY = firstRowY + index * rowHeight;
            this.optionButtons.Add(new ClickableComponent(new Rectangle(x + (this.width - buttonWidth) / 2, rowY, buttonWidth, 44), $"wait_{this.minutes[index]}")
            {
                myID = index,
                upNeighborID = index > 0 ? index - 1 : -1,
                downNeighborID = index < this.minutes.Count - 1 ? index + 1 : -1,
                leftNeighborID = -1,
                rightNeighborID = -1
            });
        }

        this.populateClickableComponentList();
        this.snapToDefaultClickableComponent();
    }

    public override void populateClickableComponentList()
    {
        this.allClickableComponents ??= new List<ClickableComponent>();
        this.allClickableComponents.Clear();
        this.allClickableComponents.AddRange(this.optionButtons);
        if (this.upperRightCloseButton != null)
            this.allClickableComponents.Add(this.upperRightCloseButton);
    }

    public override void snapToDefaultClickableComponent()
    {
        if (this.optionButtons.Count > 0)
        {
            this.currentlySnappedComponent = this.optionButtons[0];
            this.snapCursorToCurrentSnappedComponent();
        }
        else
        {
            base.snapToDefaultClickableComponent();
        }
    }

    public override void receiveKeyPress(Keys key)
    {
        if (Game1.options.doesInputListContain(Game1.options.menuButton, key))
        {
            Game1.playSound("bigDeSelect");
            this.exitThisMenu();
            return;
        }

        base.receiveKeyPress(key);
    }

    public override void receiveGamePadButton(Buttons button)
    {
        if (button == Buttons.B)
        {
            Game1.playSound("bigDeSelect");
            this.exitThisMenu();
            return;
        }

        if (button == Buttons.A && this.currentlySnappedComponent != null)
        {
            if (this.currentlySnappedComponent.myID == CloseButtonId)
            {
                Game1.playSound("bigDeSelect");
                this.exitThisMenu();
                return;
            }

            if (this.currentlySnappedComponent.myID >= 0 && this.currentlySnappedComponent.myID < this.minutes.Count)
            {
                int selectedMinutes = this.minutes[this.currentlySnappedComponent.myID];
                Game1.playSound("smallSelect");
                this.exitThisMenu();
                this.onPickMinutes(selectedMinutes);
                return;
            }
        }

        base.receiveGamePadButton(button);
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        if (this.upperRightCloseButton != null && this.upperRightCloseButton.containsPoint(x, y))
        {
            Game1.playSound("bigDeSelect");
            this.exitThisMenu();
            return;
        }

        for (int index = 0; index < this.optionButtons.Count; index++)
        {
            if (!this.optionButtons[index].containsPoint(x, y))
                continue;

            int selectedMinutes = this.minutes[index];
            Game1.playSound("smallSelect");
            this.exitThisMenu();
            this.onPickMinutes(selectedMinutes);
            return;
        }

        base.receiveLeftClick(x, y, playSound);
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        this.upperRightCloseButton?.tryHover(x, y, 0.25f);
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);
        drawTextureBox(b, this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White);

        const string title = "Wait how long?";
        Vector2 titleSize = Game1.dialogueFont.MeasureString(title);
        b.DrawString(
            Game1.dialogueFont,
            title,
            new Vector2(this.xPositionOnScreen + (this.width - titleSize.X) / 2f, this.yPositionOnScreen + 20),
            Game1.textColor);

        for (int index = 0; index < this.optionButtons.Count; index++)
        {
            Rectangle bounds = this.optionButtons[index].bounds;
            drawTextureBox(b, bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White);

            string label = FormatMinutes(this.minutes[index]);
            Vector2 labelSize = Game1.smallFont.MeasureString(label);
            b.DrawString(
                Game1.smallFont,
                label,
                new Vector2(bounds.X + (bounds.Width - labelSize.X) / 2f, bounds.Y + (bounds.Height - labelSize.Y) / 2f + 2f),
                Game1.textColor);
        }

        this.upperRightCloseButton?.draw(b);
        this.drawMouse(b);
    }

    private static string FormatMinutes(int minutes)
    {
        if (minutes % 60 == 0)
        {
            int hours = minutes / 60;
            return hours == 1 ? "1 hour" : $"{hours} hours";
        }

        int hourPart = minutes / 60;
        int minutePart = minutes % 60;
        return hourPart > 0 ? $"{hourPart}h {minutePart}m" : $"{minutePart} minutes";
    }
}
