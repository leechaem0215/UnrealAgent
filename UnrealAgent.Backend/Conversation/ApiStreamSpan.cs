using System.Text;
using Anthropic.Models.Messages;
using UnrealAgent.Backend.Core;
using Block = UnrealAgent.Backend.Core.Block;

namespace UnrealAgent.Backend.Conversation;


/// <summary>
/// API 호출 1회의 스트리밍 응답 파싱하고 누적
/// Process()로 이벤트를 먹이고 Complete()로 결과 확정
/// API를 한번 보내는거에 대해서 처리해줌
/// </summary>
public class ApiStreamSpan
{
    /// <summary>현재 진행 중인 블록의 종류와 상태</summary>
    private abstract record ActiveBlock
    {
        public sealed record Text : ActiveBlock; // 텍스트 응답 블록

        public sealed record Thinking : ActiveBlock; // 사고 과정 블록
    }
    
    // ────── 스트리밍 중 상태 ──────

    private ActiveBlock? CurrentBlock; // 현재 처리 중인 블록. (null이면 블록 사이 유휴 상태)
    
    private readonly StringBuilder TextBuffer = new(); // 텍스트 델타를 누적하는 버퍼
    private readonly StringBuilder ThinkingBuffer = new(); // 사고 과정 델타를 누적하는 버퍼
    
    private string? ThinkingSignature; // 사고 블록의 서명 (사고 델타 이후 별도 델타로 도착)
    
    // ────── 턴 완료 후 결과 ──────
    
    public StopReason? FinalStopReason; // 메시지 레벨 종료 사유 (EndSpan, ToolUse, PauseTurn 등)
    
    // 완성된 어시스턴트 콘텐츠 블록 목록입니다 (텍스트, 사고, 도구 호출)
    public IReadOnlyList<Block> Blocks => AssistantBlocks;
    private readonly List<Block> AssistantBlocks = [];
    
    // 일반 함수   
    //// 스트리밍 이벤트 하나 처리
    //// 클라이언트에 전달할 ChatEvent가 있으면 반환, 없으면 null 반환

    // ChatEvent를 반환함
    // ChatEvent : 메시지가 여러 형식으로 오는데 그걸 Thinking or Text or Tool 이런걸 Process 통해서 매번 알려줘야함
    // 그걸 위한 전용 클래스
    public ChatEvent? Process(RawMessageStreamEvent Event)
    {
        
        // 1. 콘텐츠 블록 시작 - text / thinking 블록이 새로 열림 (컨텐츠 블록이 뭐냐)
        if (Event.TryPickContentBlockStart(out RawContentBlockStartEvent? StartEvt))
            return ProcessBlockStart(StartEvt);
        
        
        // 2. 콘텐츠 블록 델타 - 텍스트 조각이 스트리밍으로 도착
        if (Event.TryPickContentBlockDelta(out RawContentBlockDeltaEvent? DeltaEvt))
            return ProcessDelta(DeltaEvt);
        
        // 3. 콘텐츠 블록 종료 - 하나의 블록 스트리밍 완료
        if (Event.TryPickContentBlockStop(out RawContentBlockStopEvent? BlockStopEvt))
            return ProcessBlockStop();
        
        // 4. 메시지 시작 - 응답 전체의 시작, 토큰 사용량(Usage) 등 메타 정보 포함
        if (Event.TryPickStart(out RawMessageStartEvent? StartMsgEvt))
            return ProcessMessageStart(StartMsgEvt);
        
        // 5. 메시지 델타 - 응답 종료 시점, stop_reason 등 최종 메타 정보 포함
        if (Event.TryPickDelta(out RawMessageDeltaEvent? MsgDelta))
            return ProcessMessageDelta(MsgDelta);

        return null;
    }
    
    // 메시지 시작 이벤트를 처리, 캐시 포함 전체 입력 토큰 수 캡쳐
    private ChatEvent? ProcessMessageStart(RawMessageStartEvent StartMsgEvt)
    {
        return null;
    }
    
    // 메시지 델타 이벤트 처리, 종료 사유 캡처
    private ChatEvent? ProcessMessageDelta(RawMessageDeltaEvent MsgDelta)
    {
        if (MsgDelta.Delta.StopReason is { } Reason)
            FinalStopReason = Reason;
        
        return null;
    }
    
    
    // 블록 시작 이벤트 처리 , 블록 종류 식별 CurrentBlock 상태 설정
    private ChatEvent? ProcessBlockStart(RawContentBlockStartEvent StartEvt)
    {
        if (StartEvt.ContentBlock.TryPickText(out _)) // 어떤거 파싱해야하는지 CurrentBlock은 기록
            CurrentBlock = new ActiveBlock.Text();
        else if (StartEvt.ContentBlock.TryPickThinking(out _))
            CurrentBlock = new ActiveBlock.Thinking(); // ActiveBlock은? 그냥 래핑 해놓은것 진행중인 블록이 뭔지 알기 위함

        return null;
    }
    
    
    /// 콘텐츠 블록 델타를 처리, 
    /// CurrentBlock 상태에 따라 적절한 버퍼에 누적, 텍스트/사고 델타는 ChatEvent로 즉시 반환
    private ChatEvent? ProcessDelta(RawContentBlockDeltaEvent DeltaEvt)
    {
        switch (CurrentBlock)
        {
            case ActiveBlock.Text when DeltaEvt.Delta.TryPickText(out TextDelta? TextDelta):
                TextBuffer.Append(TextDelta.Text);
                return new ChatEvent.Text(TextDelta.Text);
            
            case ActiveBlock.Thinking when DeltaEvt.Delta.TryPickThinking(out ThinkingDelta? ThinkingDelta):
                ThinkingBuffer.Append(ThinkingDelta.Thinking);
                return new ChatEvent.Thinking(ThinkingDelta.Thinking);
            
            // Signature 델타는 Thinking 블록 내에서 사고 델타 이후에 도착합니다.
            case ActiveBlock.Thinking when DeltaEvt.Delta.TryPickSignature(out SignatureDelta? SigDelta):
                ThinkingSignature = SigDelta.Signature;
                return null;
            
            default:
                return null;
        }
    }


    /// <summary>
    /// 블록 종료 이벤트를 처리. 
    /// 누적된 버퍼를 확정하여 AssistantBlocks에 도메인 Block으로 추가
    /// </summary>
    private ChatEvent? ProcessBlockStop()
    {
        switch (CurrentBlock)
        {
            case ActiveBlock.Text:
            {
                if (TextBuffer.Length > 0)
                {
                    AssistantBlocks.Add(new Block.Text(TextBuffer.ToString()));
                    TextBuffer.Clear();
                }
                break;
            }

            case ActiveBlock.Thinking:
            {
                if (ThinkingBuffer.Length > 0)
                {
                    AssistantBlocks.Add(new Block.Thinking(ThinkingBuffer.ToString(), ThinkingSignaturef;
                    ThinkingBuffer.Clear();
                    ThinkingSignature = null;
                }
                break;
            }
        }
        
        CurrentBlock = null;
        return null;
    }
}