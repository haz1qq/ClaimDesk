using ClaimsApi.Domain;

namespace ClaimsApi.Api;

public record UserDto(Guid Id, string Name, Role Role, Market? Market);
