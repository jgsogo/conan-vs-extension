using System;
using System.Windows.Media;
using Microsoft.VisualStudio.TaskRunnerExplorer;
using Microsoft.VisualStudio.Shell;

namespace Conan.VisualStudio.TaskRunner
{
    internal abstract class TaskRunnerConfigBase : ITaskRunnerConfig
    {
        private static ImageSource SharedIcon;
        private BindingsPersister _bindingsPersister;
        private ITaskRunnerCommandContext _context;

        protected TaskRunnerConfigBase(TaskRunnerProvider provider, ITaskRunnerCommandContext context, ITaskRunnerNode hierarchy)
        {
            _bindingsPersister = new BindingsPersister(provider);
            TaskHierarchy = hierarchy;
            _context = context;
        }

        /// <summary>
        /// TaskRunner icon
        /// </summary>
        public virtual ImageSource Icon => SharedIcon ?? (SharedIcon = LoadRootNodeIcon());

        public ITaskRunnerNode TaskHierarchy { get; }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public string LoadBindings(string configPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return _bindingsPersister.Load(configPath);
            }
            catch
            {
                return "<binding />";
            }
        }

        public bool SaveBindings(string configPath, string bindingsXml)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return _bindingsPersister.Save(configPath, bindingsXml);
            }
            catch
            {
                return false;
            }
        }

        protected virtual void Dispose(bool isDisposing)
        {
        }

        protected virtual ImageSource LoadRootNodeIcon()
        {
            return null;
        }
    }
}
