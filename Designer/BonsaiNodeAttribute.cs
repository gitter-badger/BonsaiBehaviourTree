﻿
using System;

namespace Bonsai.Designer
{
  [AttributeUsage(AttributeTargets.Class)]
  public class BonsaiNodeAttribute : Attribute
  {
    public readonly string menuPath, texturePath;

    public BonsaiNodeAttribute(string menuPath, string texturePath = null)
    {
      this.menuPath = menuPath;
      this.texturePath = texturePath;
    }
  }
}