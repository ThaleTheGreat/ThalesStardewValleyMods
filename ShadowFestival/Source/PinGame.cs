using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using StardewValley.Minigames;
using System;
using System.Collections.Generic;
using xTile.Dimensions;

namespace ShadowFestival;

public class PinGame : IMinigame
{
  private float PlayerPosition = 0.5f;
  private int CurrentStage = -1;
  private int TotalStages = 6;
  private double GameLengthSeconds = 20.0;
  private double StartTime = 0.0;
  private double LastUpdate = 0.0;
  private string CurrentDist = "";
  private int CurrentDirection = 0;
  public float nextHint = 0.0f;
  protected int _lastCurrentDirection = 0;
  public float nextRiffRaff = 0.0f;
  public float currentStepTimer = 0.0f;
  public float gameStartTimer;
  public int gameStartCount;
  protected List<PinGame.Callout> _callouts;
  protected float _gameEndTimer = 0.0f;
  protected bool _endGame;
  public static HashSet<int> claimedPrizes = new HashSet<int>();

  public PinGame()
  {
    Game1.player.CanMove = false;
    ((Character) Game1.player).FacingDirection = 0;
    this._callouts = new List<PinGame.Callout>();
    this.nextRiffRaff = 0.0f;
    this.StartGame();
  }

  public void StartGame()
  {
    while ((double) this.PlayerPosition > 0.34999999403953552 && (double) this.PlayerPosition < 0.64999997615814209)
      this.PlayerPosition = (float) Game1.random.NextDouble();
    this.StartTime = Game1.currentGameTime.TotalGameTime.TotalSeconds;
    Game1.player.canOnlyWalk = false;
    Game1.player.setRunning(false, true);
    Game1.player.canOnlyWalk = true;
    this.UpdatePlayer();
    this.UpdateHint();
    this.CurrentStage = 0;
    this.nextHint = 0.5f;
    this.gameStartTimer = 1f;
    this.gameStartCount = 4;
  }

  public void EndGame()
  {
    if (this.CurrentStage >= this.TotalStages)
    {
      float num = Math.Abs(this.PlayerPosition - 0.5f);
      if (((double) num >= 0.005 || !this.ClaimPrizeOrPass(373, "discoverMineral")) && ((double) num >= 0.05 || !this.ClaimPrizeOrPass(305, "newArtifact")) && ((double) num >= 0.1 || !this.ClaimPrizeOrPass(203, "newArtifact")) && ((double) num >= 0.25 || !this.ClaimPrizeOrPass(78, "hoeHit")))
      {
        this.ClaimPrizeOrPass(747, "slimeHit");
        PinGame.claimedPrizes.Remove(747);
      }
      if (ModEntry.setGoblinNosePosition != null)
        ModEntry.setGoblinNosePosition(((Character) Game1.player).Position);
    }
    this.UpdatePlayer();
    this.unload();
    Game1.player.forceCanMove();
    Game1.player.canOnlyWalk = false;
  }

  public bool ClaimPrizeOrPass(int item_index, string sound)
  {
    if (PinGame.claimedPrizes == null)
      PinGame.claimedPrizes = new HashSet<int>();
    if (PinGame.claimedPrizes.Contains(item_index))
      return false;
    Game1.player.addItemByMenuIfNecessary((Item) new StardewValley.Object(item_index.ToString(), 1, false, -1, 0), (ItemGrabMenu.behaviorOnItemSelect) null, false);
    Game1.playSound(sound);
    PinGame.claimedPrizes.Add(item_index);
    return true;
  }

  public void UpdatePlayer()
  {
    if (this.gameStartCount > 0)
      return;
    float num = (float) ((Game1.currentGameTime.TotalGameTime.TotalSeconds - this.StartTime) / this.GameLengthSeconds);
    ((Character) Game1.player).Position = new Vector2(Math.Min(26f, Math.Max(22f, MathHelper.Lerp(22f, 26f, this.PlayerPosition))) * 64f, Math.Min(21f, Math.Max(20f, MathHelper.Lerp(21f, 20f, num))) * 64f);
  }

  public void UpdateHint()
  {
    if (this.CurrentStage >= 0 && this.CurrentStage <= this.TotalStages)
    {
      Game1.playSound("shadowpeep");
      string str1 = (double) this.PlayerPosition > 0.5 ? "left" : "right";
      float num = Math.Abs(this.PlayerPosition - 0.5f);
      string str2;
      if ((double) num < 0.005)
        str2 = "OnTarget";
      else if ((double) num < 0.1)
        str2 = $"Close.{Game1.random.Next(3)}";
      else if ((double) num < 0.25)
        str2 = $"Medium.{Game1.random.Next(3)}";
      else
        str2 = $"Far.{Game1.random.Next(3)}";
      string str3 = str2;
      this._callouts.Add(new PinGame.Callout()
      {
        calloutText = ModEntry.Instance.Helper.Translation.Get("PinGame.Hint." + str3, (object) new
        {
          direction = str1
        }).ToString()
      });
    }
    if (this.CurrentStage <= this.TotalStages)
      this.CurrentDist = ModEntry.Instance.Helper.Translation.Get("PinGame.Distance", (object) new
      {
        d = (1 + this.TotalStages - this.CurrentStage)
      }).ToString();
    this.LastUpdate = Game1.currentGameTime.TotalGameTime.TotalSeconds;
  }

  public bool tick(GameTime time)
  {
    TimeSpan timeSpan;
    if ((double) this._gameEndTimer > 0.0)
    {
      double gameEndTimer = (double) this._gameEndTimer;
      timeSpan = time.ElapsedGameTime;
      double totalSeconds = timeSpan.TotalSeconds;
      this._gameEndTimer = (float) (gameEndTimer - totalSeconds);
      if ((double) this._gameEndTimer <= 0.0)
        this._endGame = true;
    }
    this.UpdatePlayer();
    if (this._endGame)
    {
      this._endGame = false;
      this.EndGame();
      return true;
    }
    if (this.gameStartCount > 0)
    {
      double gameStartTimer = (double) this.gameStartTimer;
      timeSpan = time.ElapsedGameTime;
      double totalSeconds = timeSpan.TotalSeconds;
      this.gameStartTimer = (float) (gameStartTimer - totalSeconds);
      if ((double) this.gameStartTimer <= 0.0)
      {
        this.gameStartTimer = 1f;
        --this.gameStartCount;
        if (this.gameStartCount == 0)
        {
          Game1.playSound("whistle");
        }
        else
        {
          ICue cue = Game1.soundBank.GetCue("clam_tone");
          cue.SetVariable("Pitch", 1200 - 100 * this.gameStartCount);
          cue.Play();
        }
      }
      return false;
    }
    if (this.CurrentStage < 0)
      return false;
    if (this.CurrentStage > 0 && this.CurrentStage <= this.TotalStages)
    {
      if ((double) this.nextHint <= 0.0)
      {
        this.UpdateHint();
        this.nextHint = 2.75f;
      }
      else
      {
        double nextHint = (double) this.nextHint;
        timeSpan = time.ElapsedGameTime;
        double totalSeconds = timeSpan.TotalSeconds;
        this.nextHint = (float) (nextHint - totalSeconds);
      }
    }
    timeSpan = Game1.currentGameTime.TotalGameTime;
    if (timeSpan.TotalSeconds > this.StartTime + this.GameLengthSeconds * ((double) this.CurrentStage / (double) this.TotalStages))
    {
      ++this.CurrentStage;
      if (this.CurrentStage == this.TotalStages + 1)
      {
        this._callouts.Clear();
        Game1.playSound("axe");
        this._gameEndTimer = 4f;
        this.CurrentDist = ModEntry.Instance.Helper.Translation.Get("PinGame.End.0", (object) new
        {
          d = $"{100f * Math.Abs(this.PlayerPosition - 0.5f):0.#}"
        }).ToString();
      }
    }
    if (this.CurrentStage > 0 && this.CurrentStage <= this.TotalStages)
    {
      if ((double) this.nextRiffRaff <= 0.0)
      {
        this.nextRiffRaff = (float) (0.30000001192092896 + Game1.random.NextDouble() * 0.25);
        this._callouts.Add(new PinGame.Callout()
        {
          calloutText = ModEntry.Instance.Helper.Translation.Get("PinGame.RiffRaff." + Game1.random.Next(11).ToString()).ToString(),
          lifeTime = 1.25f
        });
      }
      else
      {
        double nextRiffRaff = (double) this.nextRiffRaff;
        timeSpan = time.ElapsedGameTime;
        double totalSeconds = timeSpan.TotalSeconds;
        this.nextRiffRaff = (float) (nextRiffRaff - totalSeconds);
      }
    }
    for (int index = 0; index < this._callouts.Count; ++index)
    {
      PinGame.Callout callout = this._callouts[index];
      callout.Update(time);
      if ((double) callout.age >= (double) callout.lifeTime)
      {
        this._callouts.RemoveAt(index);
        --index;
      }
    }
    if (this.CurrentDirection != 0)
    {
      float num = 1f / 400f;
      if (this.CurrentDirection == -1)
        this.PlayerPosition = Math.Max(0.0f, this.PlayerPosition - num);
      else if (this.CurrentDirection == 1)
        this.PlayerPosition = Math.Min(1f, this.PlayerPosition + num);
    }
    foreach (PinGame.Callout callout in this._callouts)
    {
      callout.drawPosition.X += (float) this.CurrentDirection * -1f;
      callout.drawPosition.Y += 0.3f;
    }
    if (this.CurrentStage > 0 && this.CurrentStage <= this.TotalStages)
    {
      float num = 1f;
      this.currentStepTimer += (float) time.ElapsedGameTime.TotalSeconds;
      if (this.CurrentDirection != 0)
        num = 0.25f;
      if ((double) this.currentStepTimer >= (double) num || this._lastCurrentDirection != this.CurrentDirection && this.CurrentDirection != 0)
      {
        Game1.playSound("stoneStep");
        this.currentStepTimer = 0.0f;
      }
    }
    this._lastCurrentDirection = this.CurrentDirection;
    return false;
  }

  public bool overrideFreeMouseMovement() => true;

  public bool doMainGameUpdates() => false;

  public void receiveLeftClick(int x, int y, bool playSound = true)
  {
    if (this.CurrentStage < this.TotalStages)
      return;
    this._endGame = true;
  }

  public void receiveRightClick(int x, int y, bool playSound = true)
  {
  }

  public void leftClickHeld(int x, int y)
  {
  }

  public void releaseLeftClick(int x, int y)
  {
  }

  public void releaseRightClick(int x, int y)
  {
  }

  public void receiveKeyPress(Keys k)
  {
    if (k.Equals((object) (Keys) 27))
    {
      this._endGame = true;
    }
    else
    {
      if (this.CurrentStage < 0)
        return;
      if (Game1.options.doesInputListContain(Game1.options.moveLeftButton, k))
        this.CurrentDirection = -1;
      else if (Game1.options.doesInputListContain(Game1.options.moveRightButton, k))
        this.CurrentDirection = 1;
    }
  }

  public void receiveKeyRelease(Keys k)
  {
    if (this.CurrentDirection == -1 && Game1.options.doesInputListContain(Game1.options.moveLeftButton, k))
    {
      this.CurrentDirection = 0;
    }
    else
    {
      if (this.CurrentDirection != 1 || !Game1.options.doesInputListContain(Game1.options.moveRightButton, k))
        return;
      this.CurrentDirection = 0;
    }
  }

  public void draw(SpriteBatch b)
  {
    b.Begin((SpriteSortMode) 0, (BlendState) null, (SamplerState) null, (DepthStencilState) null, (RasterizerState) null, (Effect) null, new Matrix?());
    if (this.gameStartCount > 0)
    {
      Game1.drawWithBorder(ModEntry.Instance.Helper.Translation.Get("PinGame.Instructions.0").ToString(), Game1.textColor, Color.LimeGreen, new Vector2(50f, 50f));
      Game1.drawWithBorder(ModEntry.Instance.Helper.Translation.Get("PinGame.Instructions.1").ToString(), Game1.textColor, Color.LimeGreen, new Vector2(50f, 100f));
    }
    else if (this.CurrentStage > this.TotalStages)
      Game1.drawWithBorder(this.CurrentDist, Game1.textColor, Color.LimeGreen, new Vector2(50f, 50f));
    foreach (PinGame.Callout callout in this._callouts)
      callout.Draw(b);
    ((AnimatedSprite) Game1.player.FarmerSprite).draw(b, new Vector2(100f, 100f), 0.5f);
    b.End();
  }

  public void changeScreenSize()
  {
  }

  public void unload()
  {
  }

  public void receiveEventPoke(int data) => throw new NotImplementedException();

  public string minigameId() => "ShadwFestival.PinGame";

  public bool forceQuit() => true;

  public class Callout
  {
    public float age = 0.0f;
    public float lifeTime = 2.5f;
    public float fadeTime = 0.25f;
    public string calloutText;
    public Vector2 position;
    public float shakeTimer;
    public Vector2 velocity;
    public Vector2 drawPosition;

    public Callout()
    {
      this.drawPosition.X = (float) Game1.random.NextDouble() * (float) Game1.viewport.Width;
      this.drawPosition.Y = (float) Game1.random.NextDouble() * (float) Game1.viewport.Height;
      this.velocity.X = (float) Game1.random.NextDouble();
      this.velocity.Y = (float) Game1.random.NextDouble();
      if ((double) this.velocity.LengthSquared() > 0.0)
      {
        this.velocity.Normalize();
        this.velocity = (this.velocity * (float) Game1.random.NextDouble() * 0.5f);
      }
      this.shakeTimer = 0.5f;
    }

    public void Draw(SpriteBatch b)
    {
      float num1 = Math.Max(Math.Min(1f, (this.lifeTime - this.age) / this.fadeTime), 0.0f);
      float num2 = (float) (SpriteText.getWidthOfString(this.calloutText, 999999) + 32);
      float num3 = (float) (SpriteText.getHeightOfString(this.calloutText, 999999) + 32);
      if ((double) this.drawPosition.X < (double) num2 / 2.0)
        this.drawPosition.X = num2 / 2f;
      if ((double) this.drawPosition.X > (double) Game1.viewport.Width - (double) num2 / 2.0)
        this.drawPosition.X = (float) Game1.viewport.Width - num2 / 2f;
      if ((double) this.drawPosition.Y < (double) num3 / 2.0)
        this.drawPosition.Y = num3 / 2f;
      if ((double) this.drawPosition.Y > (double) Game1.viewport.Height - (double) num3 / 2.0)
        this.drawPosition.Y = (float) Game1.viewport.Height - num3 / 2f;
      Vector2 zero = Vector2.Zero;
      if ((double) this.shakeTimer > 0.0)
      {
        zero.X = (float) (Game1.random.Next(-1, 2) * 2);
        zero.Y = (float) (Game1.random.Next(-1, 2) * 2);
      }
      SpriteText.drawStringWithScrollCenteredAt(b, this.calloutText, (int) ((double) this.drawPosition.X + (double) zero.X), (int) ((double) this.drawPosition.Y + (double) zero.Y), (int) num2, num1, new Color?(Game1.textColor), 1, 0.88f, false);
    }

    public void Update(GameTime time)
    {
      this.age += (float) time.ElapsedGameTime.TotalSeconds;
      if ((double) this.shakeTimer > 0.0)
        this.shakeTimer -= (float) time.ElapsedGameTime.TotalSeconds;
      this.drawPosition = (this.drawPosition + this.velocity);
    }
  }
}
