// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.Drawing;
using System.Reflection;

namespace System.Windows.Forms.Tests;

public class FontCacheTests
{
    [Fact]
    public void Height_CachedAcrossScopes()
    {
        using Font font = new(FontFamily.GenericSansSerif, 12f);
        using FontCache cache = new();
        using var firstScope = cache.GetEntry(font).CreateScope();
        FieldInfo cachedHeight = typeof(FontCache.Data).GetField("_tmHeight", BindingFlags.Instance | BindingFlags.NonPublic);

        int height = firstScope.Data.Height;
        Assert.True(height > 0);

        using var secondScope = cache.GetEntry(font).CreateScope();
        Assert.Equal(height, cachedHeight.GetValue(secondScope.Data));
        Assert.Equal(height, secondScope.Data.Height);
    }

    [Fact]
    public void Height_ConcurrentReads_CachesHeight()
    {
        using Font font = new(FontFamily.GenericSansSerif, 12f);
        using FontCache cache = new();
        using var scope = cache.GetEntry(font).CreateScope();
        FontCache.Data data = scope.Data;
        int[] heights = new int[8];

        Parallel.For(0, heights.Length, i => heights[i] = data.Height);

        Assert.True(heights[0] > 0);
        Assert.All(heights, height => Assert.Equal(heights[0], height));
        FieldInfo cachedHeight = typeof(FontCache.Data).GetField("_tmHeight", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.Equal(heights[0], cachedHeight.GetValue(scope.Data));
    }

    [Fact(Skip = "Run manually, takes a few minutes and is very resource intensive.")]
    public void StressTest()
    {
        Font[] fonts = new Font[10];
        for (int i = 0; i < 10; i++)
        {
            fonts[i] = new Font("Arial", i + 6);
        }

        FontCache cache = new(softLimit: 4, hardLimit: 8);
        Random random = new();
        try
        {
            for (int i = 0; i < 10_000; i++)
            {
                Thread.Sleep(random.Next(5));
                Task.Run(() =>
                {
                    using var hfont = cache.GetEntry(
                        fonts[random.Next(10)],
                        (FONT_QUALITY)random.Next(7));

                    Assert.False(hfont.Object.IsNull);
                    Thread.Sleep(random.Next(10));
                }, TestContext.Current.CancellationToken);
            }
        }
        finally
        {
            cache.Dispose();
            for (int i = 0; i < 10; i++)
            {
                fonts[i]?.Dispose();
            }
        }
    }
}
