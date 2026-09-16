namespace Namorix.Scout.Dtos;

// A desktop user, as the addon can see them. Email is deliberately absent: it is a match key on
// the desktop's side and never comes back, so an address cannot be confirmed by probing.
public sealed record ScoutUserDto(int UserId, string Username, string Name);
