using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;

namespace WaitMenu;

public sealed class WaitSelectMenu : IClickableMenu
{
    private readonly Action<int> onPickMinutes;
    private readonly List<ClickableComponent> optionButtons = new();
    private readonly List<int> minutes = new();
    private readonly bool previousMouseVisible;
    private bool titleInPosition = true;

    public WaitSelectMenu(Action<int> onPickMinutes, int maxWaitMinutes)
    {
        this.onPickMinutes = onPickMinutes;
        this.previousMouseVisible = Game1.game1.IsMouseVisible;
        Game1.game1.IsMouseVisible = false;

        maxWaitMinutes = Math.Clamp(maxWaitMinutes, 30, 240);
        maxWaitMinutes -= maxWaitMinutes % 30;
        if (maxWaitMinutes < 30)
            maxWaitMinutes = 30;

        for (int value = 30; value <= maxWaitMinutes; value += 30)
            this.minutes.Add(value);

        int sidePadding = IClickableMenu.borderWidth * 2;
        int buttonHeight = Math.Max(44, Game1.smallFont.LineSpacing + 20);
        int rowGap = Math.Max(8, Game1.smallFont.LineSpacing / 4);
        int rowHeight = buttonHeight + rowGap;
        int buttonWidth = Math.Max(320, (int)Game1.smallFont.MeasureString("4 hours").X + IClickableMenu.spaceToClearSideBorder * 4);

        int titleAreaHeight = Math.Max(80, IClickableMenu.borderWidth + Game1.smallFont.LineSpacing);
        int menuWidth = buttonWidth + sidePadding;
        int menuHeight = titleAreaHeight + this.minutes.Count * rowHeight + IClickableMenu.borderWidth;
        int x = (Game1.uiViewport.Width - menuWidth) / 2;
        int y = (Game1.uiViewport.Height - menuHeight) / 2;

        this.xPositionOnScreen = x;
        this.yPositionOnScreen = y;
        this.width = menuWidth;
        this.height = menuHeight;

        int firstRowY = y + titleAreaHeight;
        for (int index = 0; index < this.minutes.Count; index++)
        {
            int rowY = firstRowY + index * rowHeight;
            this.optionButtons.Add(new ClickableComponent(new Rectangle(x + (this.width - buttonWidth) / 2, rowY, buttonWidth, buttonHeight), $"wait_{this.minutes[index]}")
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
            int index = this.currentlySnappedComponent.myID;
            if (index >= 0 && index < this.minutes.Count)
            {
                int selectedMinutes = this.minutes[index];
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

    public override void draw(SpriteBatch b)
    {
        _ = this.titleInPosition;

        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.5f);

        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            this.xPositionOnScreen,
            this.yPositionOnScreen,
            this.width,
            this.height,
            Color.White,
            1f,
            false);

        const string title = "Wait how long?";
        SpriteText.drawStringHorizontallyCenteredAt(
            b,
            title,
            this.xPositionOnScreen + this.width / 2,
            this.yPositionOnScreen + 20,
            999999,
            -1,
            999999,
            1f,
            0.88f);

        for (int index = 0; index < this.optionButtons.Count; index++)
        {
            Rectangle bounds = this.optionButtons[index].bounds;

            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                Color.White,
                1f,
                false);

            string label = FormatMinutes(this.minutes[index]);
            Vector2 labelSize = Game1.smallFont.MeasureString(label);
            Utility.drawTextWithShadow(
                b,
                label,
                Game1.smallFont,
                new Vector2(bounds.X + (bounds.Width - labelSize.X) / 2f, bounds.Y + (bounds.Height - labelSize.Y) / 2f + 2f),
                Game1.textColor);
        }

        this.drawMouse(b);
    }

    protected override void cleanupBeforeExit()
    {
        Game1.game1.IsMouseVisible = this.previousMouseVisible;
        base.cleanupBeforeExit();
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
