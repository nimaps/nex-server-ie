using market.Data.Repository;
using market.Models.Domain;
using market.Models.DTO.User;
using Microsoft.EntityFrameworkCore;
using market.SystemServices.Contracts;
using System.Security.Claims;
using market.Exceptions;
using market.Services.UserService.Exceptions;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using market.Models.Enum;
using Microsoft.IdentityModel.Tokens;

public class UserService
{
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Customer> _customerRepository;
    private readonly IRepository<Staff> _staffRepository;
    private readonly IRepository<Manager> _managerRepository;
    private readonly IRepository<Panel> _panelRepository;
    private readonly IPasswordService _passwordService;
    private readonly IJwtService _jwtService;
    private readonly IWorkContext _workContext;
    private readonly IMapper _mapper;


    public UserService(
        IRepository<User> userRepository,
        IRepository<Customer> customerRepository,
        IRepository<Staff> staffRepository,
        IRepository<Manager> managerRepository,
        IRepository<Panel> panelRepository,
        IPasswordService passwordService,
        IJwtService jwtService,
        IWorkContext workContext,
        IMapper mapper
    )
    {
        _userRepository = userRepository;
        _customerRepository = customerRepository;
        _staffRepository = staffRepository;
        _managerRepository = managerRepository;
        _passwordService = passwordService;
        _panelRepository = panelRepository;
        _jwtService = jwtService;
        _workContext = workContext;
        _mapper = mapper;
    }

    public async Task<RegisterResponse> RegisterUser(RegisterInput input, CancellationToken cancellationToken)
    {
        var user = await _userRepository.TableNoTracking
            .SingleOrDefaultAsync(x => x.Email == input.Email && x.UserType == input.UserType, cancellationToken);

        if (user is not null)
            throw new AlreadyRegisteredException();

        var newUser = new User
        {
            Password = _passwordService.Hash(input.Password),
            Email = input.Email,
            UserType = input.UserType
        };

        await _userRepository.AddAsync(newUser, cancellationToken);

        var claims = new List<Claim>
        {
            new(type: "user-id", value: newUser.Id.ToString()),
            new(type: "user-type", value: newUser.UserType.ToString())
        };

        if (input.UserType == UserType.Customer)
        {
            var newCustomer = new Customer { User = newUser, Addresses = null };
            await _customerRepository.AddAsync(newCustomer, cancellationToken);
        }

        if (input.UserType == UserType.Staff)
        {
            var newStaff = new Staff { User = newUser };
            var staff = await _staffRepository.AddAsync(newStaff, cancellationToken);
            claims.Add(new(type: "staff-id", value: staff.Id.ToString()));
        }

        if (input.UserType == UserType.Manager)
        {
            var newManager = new Manager { User = newUser };
            var manager = await _managerRepository.AddAsync(newManager, cancellationToken);
            claims.Add(new(type: "manager-id", value: manager.Id.ToString()));
        }

        long expiresIn = 0;
        var token = _jwtService.GenerateToken(claims, ref expiresIn);
        return new RegisterResponse { Token = token };
    }

    public async Task<RegisterResponse> LoginUser(RegisterInput input, CancellationToken cancellationToken)
    {
        var user = await _userRepository.Table
            .Where(x => x.Email == input.Email && x.UserType == input.UserType)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new UserNotFoundException();

        if (_passwordService.Verify(input.Password, user.Password) is false)
            throw new BadCredentialsException();

        var claims = new List<Claim>
        {
            new(type: "user-id", value: user.Id.ToString()),
            new(type: "user-type", value: user.UserType.ToString())
        };

        if (input.UserType == UserType.Staff)
        {
            var staff = await CheckIsStaffExist(user.Id) ?? throw new UserNotFoundException();
            claims.Add(new Claim(type: "staff-id", value: staff.Id.ToString()));
            //todo should make panel id required in staff entity
            claims.Add(new Claim(type: "panel-id", value: staff.PanelId.ToString() ?? string.Empty));
        }
        else if (input.UserType == UserType.Manager)
        {
            var manager = await CheckIsManagerExist(user.Id) ?? throw new UserNotFoundException();
            claims.Add(new Claim(type: "manager-id", value: manager.Id.ToString()));
            claims.Add(new Claim(type: "panel-id", value: manager.Panel?.Id.ToString() ?? string.Empty));
        }

        long expiresIn = 0;
        var token = _jwtService.GenerateToken(claims, ref expiresIn);

        return new RegisterResponse { Token = token };
    }

    public async Task<Customer?> CheckIsCustomerExist(int userId)
    {
        return await _customerRepository.TableNoTracking.Where(c => c.UserId == userId).SingleOrDefaultAsync();
    }

    private async Task<Staff?> CheckIsStaffExist(int userId)
    {
        return await _staffRepository.TableNoTracking.Where(c => c.UserId == userId).SingleOrDefaultAsync();
    }

    private async Task<Manager?> CheckIsManagerExist(int userId)
    {
        return await _managerRepository.TableNoTracking
            .Where(c => c.UserId == userId)
            .Include(x => x.Panel)
            .SingleOrDefaultAsync();
    }

    public async Task<ProfileBriefResponse> GetBriefProfile(CancellationToken cancellationToken)
    {
        var userId = _workContext.GetUserId();
        var userType = _workContext.GetUserType();

        var user = await _userRepository.Table
            .Where(x => x.Id == userId && x.UserType == userType)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new UserNotFoundException();

        var userResponse = _mapper.Map<ProfileBriefResponse>(user);

        if (user.UserType == UserType.Staff)
        {
            var staff = await CheckIsStaffExist(user.Id) ?? throw new UserNotFoundException();
            var panel = await _panelRepository.TableNoTracking.SingleOrDefaultAsync(x => x.Id == staff.PanelId, cancellationToken);
            if (panel is not null)
            {
                userResponse.PanelGuid = panel.Uuid;
            }
        }

        if (user.UserType == UserType.Manager)
        {
            var manager = await _managerRepository.TableNoTracking.Where(c => c.UserId == userId).Include(m => m.Panel).SingleOrDefaultAsync() ?? throw new UserNotFoundException();
            if (manager.Panel is not null)
            {
                var panel = await _panelRepository.TableNoTracking.SingleOrDefaultAsync(x => x.Id == manager.Panel.Id, cancellationToken);
                if (panel is not null)
                {
                    userResponse.PanelGuid = panel.Uuid;
                }
            }
        }

        return userResponse;
    }

    public async Task UpdateProfileField(
        string field,
        UpdateFieldInput input,
        CancellationToken cancellationToken
    )
    {
        var userId = _workContext.GetUserId();
        var userType = _workContext.GetUserType();

        var user = await _userRepository.Table
            .Where(x => x.Id == userId && x.UserType == userType)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new UserNotFoundException();

        if (!Enum.TryParse<UserUpdateField>(
                value: field,
                ignoreCase: true,
                result: out var updateField
            )
           )
        {
            throw new BadRequestException("Field not Found!");
        }

        switch (updateField)
        {
            case UserUpdateField.FirstName:
                user.FirstName = input.FieldValue;
                break;
            case UserUpdateField.LastName:
                user.LastName = input.FieldValue;
                break;
            case UserUpdateField.MobileNumber:
                user.MobileNumber = input.FieldValue;
                break;
            case UserUpdateField.AvatarLogo:
                user.AvatarLogo = input.FieldValue;
                break;
            case UserUpdateField.Setting:
                user.Setting = input.FieldValue;
                break;
            default:
                throw new BadRequestException();
        }

        await _userRepository.UpdateAsync(entity: user, cancellationToken: cancellationToken);
    }

    public async Task<ProfileBriefResponse> CompleteProfile(ProfileInput input, CancellationToken cancellationToken)
    {
        var userId = _workContext.GetUserId();
        var userType = _workContext.GetUserType();

        var currentUser = await _userRepository.Table
            .Where(x => x.Id == userId && userType == x.UserType)
            .SingleOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("user not Found!");

        currentUser.FirstName = input.FirstName;
        currentUser.LastName = input.LastName;
        currentUser.MobileNumber = input.MobileNumber;
        currentUser.AvatarLogo = input.AvatarLogo;

        if (input.FirstName.IsNullOrEmpty() || input.LastName.IsNullOrEmpty() || input.MobileNumber.IsNullOrEmpty())
            throw new BadRequestException();

        await _userRepository.UpdateAsync(currentUser, cancellationToken);

        return _mapper.Map<ProfileBriefResponse>(currentUser);
    }
}