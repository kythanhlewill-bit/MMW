using AutoMapper;
using MMW.Application.Models;
using MMW.Domain.Entities;

namespace MMW.Application.Mappings;

public class TradeProfile : Profile
{
    public TradeProfile()
    {
        CreateMap<Trade, TradeDto>()
            .ForMember(d => d.AccountName, opt => opt.MapFrom(s => s.TradingAccount != null ? s.TradingAccount.Name : null));
        CreateMap<TradeDto, Trade>()
            .ForMember(d => d.TradingAccount, opt => opt.Ignore());
    }
}
