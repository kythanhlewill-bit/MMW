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
            .ForMember(d => d.TradingAccount, opt => opt.Ignore())
            // Chiều ngược KHÔNG được mang CreatedDate theo. Form thêm lệnh gửi lên một DTO trống
            // ngày tạo, ánh xạ thẳng sẽ ghi đè mặc định 01/01/0001 vào bản ghi mới.
            .ForMember(d => d.CreatedDate, opt => opt.Ignore());
    }
}
