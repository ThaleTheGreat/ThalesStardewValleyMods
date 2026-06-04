using Microsoft.Xna.Framework.Graphics;
using System;

namespace GMCMAdvancedSearch;

public interface IMobilePhoneApi
{
    bool AddApp(string id, string name, Action action, Texture2D icon);
}
