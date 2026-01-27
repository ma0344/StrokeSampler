using System;

namespace SkiaTester.Helpers;

public static class PencilPressureFloorTable
{
    public const double FloorForS35AndUp = 0.0105;

    // UWP pre-save alpha floor table (S=2..34). S is diameter(px).
    private static readonly double[] FloorByS =
    {
        0.0,    // 0
        0.0161, // 1
        0.0084, // 2
        0.0057, // 3
        0.0059, // 4
        0.0066, // 5
        0.0044, // 6
        0.0052, // 7
        0.0059, // 8
        0.0065, // 9
        0.0070, // 10
        0.0075, // 11
        0.0080, // 12
        0.0085, // 13
        0.0091, // 14
        0.0097, // 15
        0.0101, // 16
        0.0101, // 17
        0.0101, // 18
        0.0101, // 19
        0.0101, // 20
        0.0101, // 21
        0.0102, // 22
        0.0102, // 23
        0.0102, // 24
        0.0102, // 25
        0.0102, // 26
        0.0103, // 27
        0.0103, // 28
        0.0103, // 29
        0.0103, // 30
        0.0104, // 31
        0.0104, // 32
        0.0104, // 33
        0.0104, // 34
    };

    public static double GetPFloor(int diameterPx)
    {
        if (diameterPx <= 0) return 0.0;
        if (diameterPx >= 35) return FloorForS35AndUp;
        if (diameterPx < FloorByS.Length) return FloorByS[diameterPx];
        return 0.0;
    }
}
