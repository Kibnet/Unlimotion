using AutoMapper;
using AutoMapper.Configuration;
using Unlimotion.Interface;
using Unlimotion.Server.Domain;
using Unlimotion.Server.ServiceModel;
using Unlimotion.Server.ServiceModel.Molds;
using Unlimotion.Server.ServiceModel.Molds.Attachment;
using Unlimotion.Server.ServiceModel.Molds.Tasks;

namespace Unlimotion.Server
{
    public class AppModelMapping
    {
        // TODO : IoC IMapper
        public static Mapper ConfigureMapping()
        {
            var cfg = new MapperConfigurationExpression();
            ConfigureModelMapping(cfg);
            var mapperConfiguration = new MapperConfiguration(cfg);
            mapperConfiguration.AssertConfigurationIsValid();
            mapperConfiguration.CompileMappings();
            var mapper = new Mapper(mapperConfiguration);
            return mapper;
        }

        private static void ConfigureModelMapping(MapperConfigurationExpression cfg)
        {
            cfg.CreateMap<RepeaterPattern, RepeaterPatternMold>().ReverseMap();
            cfg.CreateMap<RepeaterPattern, RepeaterPatternHubMold>().ReverseMap();
            cfg.CreateMap<RepeaterType, RepeaterTypeMold>().ReverseMap();
            cfg.CreateMap<RepeaterType, RepeaterTypeHubMold>().ReverseMap();
            cfg.CreateMap<Attachment, AttachmentMold>();
            cfg.CreateMap<Attachment, AttachmentHubMold>();
            
            cfg.CreateMap<TaskItem, TaskItemMold>().ReverseMap();
            cfg.CreateMap<TaskItem, ReceiveTaskItem>();
            cfg.CreateMap<TaskItemHubMold, TaskItem>()
                .ForMember(m => m.UserId, e => e.Ignore())
                .ForMember(m => m.CreatedDateTime, e => e.Ignore());            

            cfg.CreateMap<User, UserProfileMold>();
            cfg.CreateMap<User, MyUserProfileMold>()
                .IncludeBase<User, UserProfileMold>()
                .ForMember(m => m.IsPasswordSetted, e => e.Ignore());
            cfg.CreateMap<SetProfile, User>()
                .ForMember(m => m.Id, e => e.Ignore())
                .ForMember(m => m.RegisteredTime, e => e.Ignore())
                .ForMember(m => m.Login, e => e.Ignore());
        }
    }
}
