namespace LogExpress.Models
{
    public class Filter
    {
        /// <summary>
        /// The path regex' for this filter.
        /// The UI by default restrains to show filters that the currently watched file matches.
        /// Default is "" which means that the filter visibility is not restrained by paths.
        /// </summary>
        public string[] Path;

        /// <summary>
        /// The groups that this filter is associated with.
        /// When selecting which filter to load, a hierarchy of groups will make it easier
        /// to select the filter the user wants.
        /// The group hierarchy is created by separating the group on '.' characters.
        /// Default is "Global".
        /// </summary>
        public string[] Group;
        
        /// <summary>
        /// The name that this filter is given, to make it easier to identify.
        /// Required.
        /// </summary>
        public string Name;
        
        /// <summary>
        /// The actual filter regular expression to use.
        /// Required.
        /// </summary>
        public string RegExp;
        
        /// <summary>
        /// The filter's regular expression options to use.
        /// Default is "".
        /// </summary>
        public string Options;
    }
}