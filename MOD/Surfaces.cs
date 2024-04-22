using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExtraAssetsImporter;

internal class Surfaces
{
    internal static string GetCatByRendererPriority(int i)
    {
        return i switch
        {
            -100 => "Ground",
            -99 => "Grass", //"Grass",
            -98 => "Sand", //"Sand",
            -97 => "Concrete",
            -96 => "Pavement", //"Pavement",
            -95 => "Tiles",
            _ => "Misc"
        };
    }
}
