namespace Namorix.Scout.Dtos;

public sealed record StreamOfferRequest(string? Sdp);

public sealed record StreamAnswerRequest(string? Sdp);

public sealed record StreamIceRequest(string? Candidate);

public sealed record StreamIceResult(IReadOnlyList<string> Candidates);
