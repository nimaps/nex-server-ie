using System.Security.Claims;
using market.Models.Enum;

namespace market.SystemServices.Contracts;

public interface IWorkContext
{
    int GetUserId();
    int GetStaffId();
    int GetManagerId();
    UserType GetUserType();
    bool IsAuthenticated();
    string GetTokenFromHeader();
    Claim GetCurrentUserTypeClaim();
    Claim GetCurrentUserIdClaim();
    Claim GetCurrentStaffIdClaim();
    Claim GetCurrentManagerIdClaim();
}