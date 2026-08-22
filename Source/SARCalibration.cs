using System.Collections.Generic;
using Verse;

namespace SizedApparelAutoRefit
{
    // Per-build correction so that a strength of 1.0 means "correctly covered" on every build.
    //
    // The geometry alone does not get there. Sized Apparel's art is not consistent between builds:
    // how far the breast overlay sits outside the garment varies with how each build's body and
    // its breast art were drawn, and in front view on some builds it overhangs considerably more
    // than the cloth actually needs to travel. These factors are measured from the game, by
    // finding the minimum slider value that just covers on each build and facing.
    //
    // Anything not listed — modded builds — gets 1.0 and behaves as it did before.
    //
    // Caveat worth knowing: a single factor per build cannot be exact at every size. The bulge
    // and coverage terms grow at different rates, so the ratio between them shifts as the bust
    // gets bigger. These are tuned at the sizes that were actually looked at.
    public static class SARCalibration
    {
        private struct Factors
        {
            public float front;
            public float side;

            public Factors(float front, float side)
            {
                this.front = front;
                this.side = side;
            }
        }

        private static readonly Dictionary<string, Factors> table = new Dictionary<string, Factors>
        {
            { "Female", new Factors(0.55f, 1.10f) },
            { "Thin",   new Factors(1.00f, 1.00f) },
            { "Hulk",   new Factors(0.375f, 1.05f) },   // 0.75 x the 0.5 measured after bulge was added
            { "Fat",    new Factors(1.00f, 1.15f) },
        };

        public static float For(string bodyType, SARPolicy.Facing facing)
        {
            if (bodyType.NullOrEmpty()) return 1f;
            if (!table.TryGetValue(bodyType, out Factors f)) return 1f;
            return facing == SARPolicy.Facing.Side ? f.side : f.front;
        }
    }
}
