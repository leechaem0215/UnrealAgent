using Anthropic.Models.Messages;
using Microsoft.Extensions.DependencyInjection;
using UnrealAgent.Backend.Auth;

ServiceCollection Services = new ServiceCollection(); // 반드시 필요
Services.AddSingleton<AuthConfig>(); // AuthConfig 유일성을 보장 (AddSingleton 싱글톤으로 등록해서)
// 싱글톤으로 생성해두면, 생성자에서 쉽게 사용 가능 // AuthConfig a = new(new test()) 해줄필요없음
// 등록하면 항상 빌드를 해야함

// 여기서 빌드 과정에서 생성자에서 뭐가 필요한지 실행시켜주는 곳임
ServiceProvider Provider = Services.BuildServiceProvider();

// 여기 메인에서 AuthConfig 사용하고싶으면 아래와 같이
AuthConfig Auth = Provider.GetRequiredService<AuthConfig>();

Auth.Load();

// 존재 확인
if (!Auth.IsApiKeyConfigured())
{
    Console.Write("API Key 입력 : ");
    string? Key = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(Key))
    {
        Console.WriteLine("API Key가 입력되지 않았습니다.");
        return;
    }
    
    Auth.SetApiKey(Key);
    Console.WriteLine("API key 저장 완료");
}

while (true)
{
    Console.Write("\n> ");
    string? Input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(Input))
        continue;

    if (Input.Equals("exit", StringComparison.OrdinalIgnoreCase))
        break;

    var Parameters = new MessageCreateParams // 메시지를 보내기 위해 필요한 것
    {
        Model = "claude-opus-5-5",
        MaxTokens =1024,
        Messages = [new MessageParam() {Role = Role.User, Content = Input}]
    };
    
    // 스트리밍 형식으로 변경
    await foreach (RawMessageStreamEvent Event in Auth.Client!.Messages.CreateStreaming(Parameters))
    {
        // TextDelta : AI가 사용자에게 답변을 주는 메시지
        if(Event.TryPickContentBlockDelta(out RawContentBlockDeltaEvent? delta) && delta.Delta.TryPickText(out TextDelta? text))
            Console.WriteLine(text.Text);
    }
}


/* while 문으로 변경하면서 주석처리한 처음 코드
매번 입력 받을거임
MessageCreateParams Parameters = new MessageCreateParams // 메시지를 보내기 위해 필요한 것
{
    Model = "claude-opus-5-5",
    MaxTokens =1024,
    Messages = [new MessageParam() {Role = Role.User, Content ="안녕하세요 자기소개 해주삼"}]
};

Message Response = await Auth.Client!.Messages.Create(Parameters);

foreach (ContentBlock Block in Response.Content)
{
    if(Block.TryPickText(out var Text))
        Console.WriteLine(Text.Text);
}
*/