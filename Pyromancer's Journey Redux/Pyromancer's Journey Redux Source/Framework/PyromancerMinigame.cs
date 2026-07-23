using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Minigames;

namespace ThaleTheGreat.PyromancersJourney.Framework
{
    internal class PyromancerMinigame : IMinigame, IDisposable
    {
        private World? World;
        private readonly bool PreviousMouseVisible;
        private readonly float PreviousMouseCursorTransparency;
        private readonly Point PreviousMousePosition;
        private bool MouseCaptureReleased;
        private bool RunResultQueued;

        public PyromancerMinigame()
        {
            this.PreviousMouseVisible = Game1.game1.IsMouseVisible;
            this.PreviousMouseCursorTransparency = Game1.mouseCursorTransparency;
            MouseState mouse = Mouse.GetState();
            this.PreviousMousePosition = mouse.Position;
            this.World = new World();
            this.CaptureMouse();
        }

        public void Dispose()
        {
            this.RestoreMouse();
            this.World?.Dispose();
            this.World = null;
        }

        public void changeScreenSize()
        {
            this.CenterMouse();
        }

        public bool doMainGameUpdates()
        {
            return false;
        }

        public void draw(SpriteBatch b)
        {
            this.HideMouseCursor();
            this.World?.Render();
            this.HideMouseCursor();
        }

        public bool forceQuit()
        {
            this.unload();
            return true;
        }

        public void leftClickHeld(int x, int y) { }

        public string minigameId()
        {
            return "PyromancerJourney";
        }

        public bool overrideFreeMouseMovement()
        {
            return true;
        }

        public void receiveEventPoke(int data) { }

        public void receiveKeyPress(Keys k)
        {
            if (k == Keys.Escape)
                this.World?.Quit();
        }

        public void receiveKeyRelease(Keys k) { }

        public void receiveLeftClick(int x, int y, bool playSound = true) { }

        public void receiveRightClick(int x, int y, bool playSound = true) { }

        public void releaseLeftClick(int x, int y) { }

        public void releaseRightClick(int x, int y) { }

        public bool tick(GameTime time)
        {
            World? world = this.World;
            if (world is null)
                return true;

            if (world.HasQuit)
            {
                this.QueueRunResult(world);
                this.RestoreMouse();
                return true;
            }

            this.HideMouseCursor();
            this.UpdateMouseLook(world);
            world.Update();

            if (world.HasQuit)
            {
                this.QueueRunResult(world);
                this.RestoreMouse();
            }
            else
                this.HideMouseCursor();

            return world.HasQuit;
        }

        public void unload()
        {
            this.Dispose();
        }

        private void QueueRunResult(World world)
        {
            if (this.RunResultQueued || !world.HasWon)
                return;

            this.RunResultQueued = true;
            Mod.Instance.QueueCompletedRun(world.UsedInfiniteHealth);
        }

        private void CaptureMouse()
        {
            this.HideMouseCursor();
            this.CenterMouse();
        }

        private void HideMouseCursor()
        {
            Game1.game1.IsMouseVisible = false;
            Game1.mouseCursorTransparency = 0f;
        }

        private void RestoreMouse()
        {
            if (this.MouseCaptureReleased)
                return;

            this.MouseCaptureReleased = true;
            Game1.game1.IsMouseVisible = this.PreviousMouseVisible;
            Game1.mouseCursorTransparency = this.PreviousMouseCursorTransparency;

            if (Game1.game1.IsActive)
            {
                Rectangle bounds = Game1.game1.Window.ClientBounds;
                Mouse.SetPosition(
                    Math.Clamp(this.PreviousMousePosition.X, 0, Math.Max(0, bounds.Width - 1)),
                    Math.Clamp(this.PreviousMousePosition.Y, 0, Math.Max(0, bounds.Height - 1))
                );
            }
        }

        private void UpdateMouseLook(World world)
        {
            if (!Game1.game1.IsActive)
                return;

            Rectangle bounds = Game1.game1.Window.ClientBounds;
            int width = Math.Max(1, bounds.Width);
            int centerX = width / 2;
            int centerY = Math.Max(1, bounds.Height) / 2;
            MouseState mouse = Mouse.GetState();
            int deltaX = mouse.X - centerX;

            if (deltaX != 0)
                world.Player.Look += deltaX * MathHelper.TwoPi / width;

            Mouse.SetPosition(centerX, centerY);
        }

        private void CenterMouse()
        {
            if (!Game1.game1.IsActive)
                return;

            Rectangle bounds = Game1.game1.Window.ClientBounds;
            Mouse.SetPosition(
                Math.Max(1, bounds.Width) / 2,
                Math.Max(1, bounds.Height) / 2
            );
        }
    }
}
