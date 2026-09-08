using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Unlimotion.Domain;
using Unlimotion.Interface;
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
            var mapperConfiguration = new MapperConfiguration(cfg, NullLoggerFactory.Instance);
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
            
            cfg.CreateMap<TaskItem, TaskItemMold>()
                .ForMember(m => m.SortOrder, e => e.Ignore());
            cfg.CreateMap<TaskItemMold, TaskItem>()
                .ForMember(m => m.ExtensionData, e => e.Ignore())
                .IgnoreComputedStatusMembers();
            cfg.CreateMap<TaskItem, ReceiveTaskItem>();
            cfg.CreateMap<TaskItemHubMold, TaskItem>()
                .ForMember(m => m.UserId, e => e.Ignore())
                .ForMember(m => m.CreatedDateTime, e => e.Ignore())
                .ForMember(m => m.ExtensionData, e => e.Ignore())
                .ForMember(
                    task => task.IsGoal,
                    options =>
                    {
                        options.PreCondition(mold =>
                            mold.TaskClassificationSchemaVersion >=
                            TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion &&
                            mold.IsGoal.HasValue);
                        options.MapFrom(mold => mold.IsGoal!.Value);
                    })
                .ForMember(
                    task => task.AreaIds,
                    options =>
                    {
                        options.PreCondition(mold =>
                            mold.TaskClassificationSchemaVersion >=
                            TaskStorageCapabilities.CurrentTaskClassificationSchemaVersion &&
                            mold.AreaIds != null);
                        options.MapFrom(mold => mold.AreaIds!);
                    })
                .IgnoreComputedStatusMembers();

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

    internal static class TaskItemMappingExpressionExtensions
    {
        public static IMappingExpression<TSource, TaskItem> IgnoreComputedStatusMembers<TSource>(
            this IMappingExpression<TSource, TaskItem> expression)
        {
            return expression
                .ForMember(task => task.IsCompleted, options => options.Ignore())
                .ForMember(task => task.CompletedDateTime, options => options.Ignore())
                .ForMember(task => task.ArchiveDateTime, options => options.Ignore());
        }
    }
}
