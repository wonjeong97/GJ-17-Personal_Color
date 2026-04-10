namespace My.Scripts.Global
{
    /// <summary>
    /// 프로젝트 전역에서 사용되는 상수 값을 관리한다.
    /// 매직 넘버 및 하드코딩된 문자열 사용을 방지하여 유지보수성을 높이기 위함.
    /// </summary>
    public static class GameConstants
    {
        public static class Scene
        {
            public const string Title = "0_Title";
            public const string Description = "1_Description";
            public const string Capture = "2_Capture";
        }
        
        public static class Path
        {
            public const string JsonSetting = "Settings.json";
        }
    }
}