using System;
using Microsoft.Xna.Framework;

namespace ThaleTheGreat.WalletScepter;

public interface ISpecialPowerAPI
{
    bool RegisterPowerCategory(string uniqueID, Func<string> displayName, string iconTexture);
    bool RegisterPowerCategory(string uniqueID, Func<string> displayName, string iconTexture, Point sourceRectPosition, Point sourceRectSize);
}
