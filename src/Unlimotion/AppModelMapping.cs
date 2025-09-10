using AutoMapper;
using Unlimotion.Domain;
using Unlimotion.Interface;
using Unlimotion.Server.ServiceModel.Molds.Attachment;
using Unlimotion.Server.ServiceModel.Molds.Tasks;

namespace Unlimotion
{
    public class AppModelMapping
    {
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
            cfg.CreateMap<AttachmentHubMold, AttachmentMold>();
            cfg.CreateMap<AttachmentMold, AttachmentHubMold>();
            cfg.CreateMap<TaskItemMold, TaskItem>().ReverseMap();
            cfg.CreateMap<RepeaterPattern, RepeaterPatternMold>().ReverseMap();
            cfg.CreateMap<RepeaterPattern, RepeaterPatternHubMold>().ReverseMap();
            cfg.CreateMap<RepeaterTypeMold, RepeaterType>().ReverseMap();
            cfg.CreateMap<ReceiveTaskItem, TaskItem>().ForMember(x => x.SortOrder, opt => opt.Ignore());
            cfg.CreateMap<TaskItem, TaskItemHubMold>();
            cfg.CreateMap<RepeaterType, RepeaterTypeHubMold>();
            cfg.CreateMap<RepeaterType, RepeaterType>().ReverseMap();
            cfg.CreateMap<RepeaterPattern, RepeaterPattern>().ReverseMap();
            cfg.CreateMap<TaskItem, TaskItem>()
                .ForMember(m => m.UserId, e => e.Ignore())
                .ReverseMap();
        }
    }
}