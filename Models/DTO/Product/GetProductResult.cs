using AutoMapper;

namespace market.Models.DTO.Product;

public class GetProductResult : GetProductShortResult
{
    public string? Description { get; set; }
    public string? Detail { get; set; }
}

public class GetProductResponseMap : Profile
{
    public GetProductResponseMap()
    {
        CreateMap<Domain.Product, GetProductShortResult>()
            .ForMember(
                des => des.Tags,
                opt => opt.MapFrom(src => src.ProductTags.Select(x => x.Tag))
            );
    }
}