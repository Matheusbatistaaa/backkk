namespace PerceptAI.API.Models
{
    
    public static class FaceMeshIndices
    {
        
        public const int RightEyeOuter = 33;
        public const int RightEyeInner = 133;
        public const int RightEyeUpper = 159;
        public const int RightEyeLower = 145;
        public const int RightEyeUpper2 = 158;
        public const int RightEyeLower2 = 153;

        public const int LeftEyeOuter = 263;
        public const int LeftEyeInner = 362;
        public const int LeftEyeUpper = 386;
        public const int LeftEyeLower = 374;
        public const int LeftEyeUpper2 = 385;
        public const int LeftEyeLower2 = 380;

        public const int RightBrowInner = 107;
        public const int LeftBrowInner = 336;

        public const int MouthRightCorner = 61;
        public const int MouthLeftCorner = 291;
        public const int UpperLipInner = 13;
        public const int LowerLipInner = 14;
        public const int MouthRightInner = 78;
        public const int MouthLeftInner = 308;

        public const int Chin = 152;
        public const int Forehead = 10;
        public const int RightCheek = 234;
        public const int LeftCheek = 454;

        public static readonly int[] InnerLipContour =
        {
            78, 191, 80, 81, 82, 13, 312, 311, 310, 415,
            308, 324, 318, 402, 317, 14, 87, 178, 88, 95
        };

        public const int TotalPoints = 468;
    }
}
