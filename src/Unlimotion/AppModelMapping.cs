using AutoMapper;
using Unlimotion.Interface;
using Unlimotion.Server.ServiceModel.Molds.Attachment;
using Unlimotion.Server.ServiceModel.Molds.Tasks;
using Unlimotion.ViewModel;

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
            cfg.CreateMap<TaskItemMold, TaskItem>();
            cfg.CreateMap<RepeaterPatternMold, RepeaterPattern>();
            cfg.CreateMap<RepeaterTypeMold, RepeaterType>();
            cfg.CreateMap<TaskItem, TaskItemHubMold>();
            cfg.CreateMap<RepeaterPattern, RepeaterPatternHubMold>();
            cfg.CreateMap<RepeaterType, RepeaterTypeHubMold>();
        }
    }
}