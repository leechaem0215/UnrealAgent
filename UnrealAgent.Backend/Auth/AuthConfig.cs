using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Anthropic;

namespace UnrealAgent.Backend.Auth;

/// <summary>
/// API Key 기반 인증 시스템
/// AuthConfig.json 파일에서 키를 로드, AuthropicClient 생성
/// </summary>
public class AuthConfig
{
    // 설정 파일 경로
    private readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
        ".unrealagent", "AuthConfig.json");
    
    // json 방식으로 관리
    // json 직렬화
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    
    // 저장된 API Key
    public string? ApiKey { get; private set; }
    
    // 현재 인증 정보로 구성된 Anthropic 클라이언트임
    public AnthropicClient? Client { get; private set; } // 이걸 통해 소통함

    // API Key 설정되어 있는지 확인
    public bool IsApiKeyConfigured() => !string.IsNullOrWhiteSpace(ApiKey);
    
    // API Key 설정 후 저장
    public void SetApiKey(string Key)
    {
        ApiKey = Key;
        Save();
    }
    
    // 현재 설정 파일에 저장, 디렉토리 없으면 생성
    private void Save()
    {
        string Dir = Path.GetDirectoryName(ConfigPath)!; // ! null 경고 무시 연산자
        if (!Directory.Exists(Dir))
            Directory.CreateDirectory(Dir);

        JsonObject Root = new() { ["api_key"] = ApiKey };
        File.WriteAllText(ConfigPath, Root.ToJsonString(JsonOptions));
        
        UpdateClient();
    }
    
    // 설정 파일 로드, 파일 없으면 빈 설정 유지
    public void Load()
    {
        if (!File.Exists(ConfigPath))
            return;
        
        string Json = File.ReadAllText(ConfigPath);
        JsonNode? Root = JsonNode.Parse(Json); // ? 붙는 이유 : null 허용

        if (Root is null) // 패턴 매칭 문법 : == 의미 같음
            return;
        
        ApiKey = Root["api_key"]?.GetValue<string>();

        UpdateClient();
    }
    
    // API Key로 AnthropicClient 생성
    private void UpdateClient()
    {
        // Load와 Save 에서 사용
        Client = ApiKey is not null ? new AnthropicClient {ApiKey = ApiKey} : null;
    }
}