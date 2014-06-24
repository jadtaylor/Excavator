﻿// <copyright>
// Copyright 2013 by the Spark Development Network
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Data;
using System.IO;
using System.Linq;
using LumenWorks.Framework.IO.Csv;
using Rock.Data;
using Rock.Model;
using Rock.Web.Cache;

namespace Excavator.CSV
{
    /// <summary>
    /// This example extends the base Excavator class to consume a CSV model.
    /// </summary>
    [Export( typeof( ExcavatorComponent ) )]
    partial class CSVComponent : ExcavatorComponent
    {
        #region Constructor

        /// <summary>
        /// Initializes a new instance of the <see cref="CSVComponent"/> class.
        /// </summary>
        public CSVComponent()
        {
        }

        #endregion

        #region Fields

        /// <summary>
        /// Gets the full name of the excavator type.
        /// </summary>
        /// <value>
        /// The name of the database being imported.
        /// </value>
        public override string FullName
        {
            get { return "CSV File"; }
        }

        /// <summary>
        /// Gets the supported file extension type(s).
        /// </summary>
        /// <value>
        /// The supported extension type.
        /// </value>
        public override string ExtensionType
        {
            get { return ".csv"; }
        }

        /// <summary>
        /// The local data store, contains Database and TableNode list
        /// because multiple files can be uploaded
        /// </summary>
        private List<CsvDataModel> CsvDataToImport { get; set; }

        /// <summary>
        /// The person assigned to do the import
        /// </summary>
        private PersonAlias ImportPersonAlias;

        /// <summary>
        /// The person entity type identifier
        /// </summary>
        private int PersonEntityTypeId;

        /// <summary>
        /// All the people who've been imported
        /// </summary>
        private Dictionary<int, string> ImportedPeople;

        /// <summary>
        /// The list of current campuses
        /// </summary>
        private List<Campus> CampusList;

        // Report progress when a multiple of this number has been imported
        private static int ReportingNumber = 50;

        #endregion

        #region Methods

        /// <summary>
        /// Loads the database for this instance.
        /// may be called multiple times, if uploading multiple CSV files.
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public override bool LoadSchema( string fileName )
        {
            //enforce that the filename must be a known configuration.
            if ( !FileIsKnown( fileName ) )
                return false;

            var dbPreview = new CsvReader( new StreamReader( fileName ), true );

            if ( CsvDataToImport == null )
            {
                CsvDataToImport = new List<CsvDataModel>();
                TableNodes = new List<DatabaseNode>();
            }

            //a local tableNode object, which will track this one of multiple CSV files that may be imported
            List<DatabaseNode> tableNodes = new List<DatabaseNode>();
            CsvDataToImport.Add( new CsvDataModel( fileName ) { TableNodes = tableNodes, RecordType = GetRecordTypeFromFilename( fileName ) } );

            var tableItem = new DatabaseNode();
            tableItem.Name = Path.GetFileNameWithoutExtension( fileName );
            int currentIndex = 0;

            var firstRow = dbPreview.ElementAtOrDefault( 0 );
            if ( firstRow != null )
            {
                foreach ( var columnName in dbPreview.GetFieldHeaders() )
                {
                    var childItem = new DatabaseNode();
                    childItem.Name = columnName;
                    childItem.NodeType = typeof( string );
                    childItem.Value = firstRow[currentIndex] ?? string.Empty;
                    childItem.Table.Add( tableItem );
                    tableItem.Columns.Add( childItem );
                    currentIndex++;
                }

                tableNodes.Add( tableItem );
                TableNodes.Add( tableItem ); //this is to maintain compatibility with the base Excavator object.
            }

            return tableNodes.Count() > 0 ? true : false;
        }

        /// <summary>
        /// Previews the data. Overrides base class because we have potential for more than one imported file
        /// </summary>
        /// <param name="tableName">Name of the table to preview.</param>
        /// <returns></returns>
        public new DataTable PreviewData( string nodeId )
        {
            foreach ( var dataNode in CsvDataToImport )
            {
                var node = dataNode.TableNodes.Where( n => n.Id.Equals( nodeId ) || n.Columns.Any( c => c.Id == nodeId ) ).FirstOrDefault();
                if ( node != null && node.Columns.Any() )
                {
                    var dataTable = new DataTable();
                    dataTable.Columns.Add( "File", typeof( string ) );
                    foreach ( var column in node.Columns )
                    {
                        dataTable.Columns.Add( column.Name, column.NodeType );
                    }

                    var rowPreview = dataTable.NewRow();
                    foreach ( var column in node.Columns )
                    {
                        rowPreview[column.Name] = column.Value ?? DBNull.Value;
                    }

                    dataTable.Rows.Add( rowPreview );
                    return dataTable;
                }
            }
            return null;
        }

        /// <summary>
        /// Transforms the data from the dataset.
        /// </summary>
        public override int TransformData( string importUser = null )
        {
            ReportProgress( 0, "Starting import..." );
            if ( !CheckExistingImport( importUser ) )
            {
                return -1;
            }

            // TODO: only import things that the user checked
            // var columnList = TableNodes.Where( n => n.Checked != false ).ToList();

            MapFamilyData();

            return 0;
        }

        /// <summary>
        /// Checks the database for existing import data.
        /// returns false if an error occurred
        /// </summary>
        private bool CheckExistingImport( string importUser )
        {
            //try
            //{
            var lookupContext = new RockContext();
            var personService = new PersonService( lookupContext );
            var importPerson = personService.GetByFullName( importUser, includeDeceased: false, allowFirstNameOnly: true ).FirstOrDefault();
            if ( importPerson == null )
            {
                importPerson = personService.Queryable().FirstOrDefault();
            }
            if ( importPerson == null )
            {
                LogException( "CheckExistingImport", "The named import user was not found, and none could be created." );
                return false;
            }

            ImportPersonAlias = new PersonAliasService( lookupContext ).Get( importPerson.Id );

            PersonEntityTypeId = EntityTypeCache.Read( "Rock.Model.Person" ).Id;
            var textFieldTypeId = FieldTypeCache.Read( new Guid( Rock.SystemGuid.FieldType.TEXT ) ).Id;

            ReportProgress( 0, "Checking for existing people..." );
            ImportedPeople = personService.Queryable().Where( p => p.ForeignId != null )
                .ToDictionary( p => p.Id, p => p.ForeignId );

            CampusList = new CampusService( lookupContext ).Queryable().ToList();
            return true;
            //}
            //catch ( Exception ex )
            //{
            //    LogException( "CheckExistingImport", ex.ToString() );
            //    return false;
            //}
        }

        #endregion

        #region File Processing Methods

        /// <summary>
        /// Gets the name of the file without the extension.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns></returns>
        private string GetFileRootName( string fileName )
        {
            var root = Path.GetFileName( fileName ).ToLower().Replace( ".csv", string.Empty );
            return root;
        }

        /// <summary>
        /// Checks if the file matches a known format.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns></returns>
        private bool FileTypeMatches( CsvDataModel.RockDataType filetype, string name )
        {
            if ( name.ToUpper().Equals( filetype.ToString() ) )
            {
                return true;
            }

            return false;
        }

        /// Checks if the file matches a known format.
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns></returns>
        private bool FileIsKnown( string fileName )
        {
            string name = GetFileRootName( fileName );
            foreach ( var filetype in Extensions.Get<CsvDataModel.RockDataType>() )
            {
                if ( FileTypeMatches( filetype, name ) )
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the record type based on the filename.
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <returns></returns>
        private CsvDataModel.RockDataType GetRecordTypeFromFilename( string filename )
        {
            string name = GetFileRootName( filename );
            foreach ( var filetype in Extensions.Get<CsvDataModel.RockDataType>() )
            {
                if ( FileTypeMatches( filetype, name ) )
                {
                    return filetype;
                }
            }

            return CsvDataModel.RockDataType.NONE;
        }

        #endregion

        #region Family Constants

        private const int FamilyId = 0;
        private const int FamilyName = 1;
        private const int FamilyLastName = 2;
        private const int Campus = 3;
        private const int Address = 4;
        private const int Address2 = 5;
        private const int City = 6;
        private const int State = 7;
        private const int Zip = 8;
        private const int Country = 9;
        private const int SecondaryAddress = 10;
        private const int SecondaryAddress2 = 11;
        private const int SecondaryCity = 12;
        private const int SecondaryState = 13;
        private const int SecondaryZip = 14;
        private const int SecondaryCountry = 15;

        #endregion

        #region Individual Constants

        private const int PersonId = 2;
        private const int Prefix = 3;
        private const int FirstName = 4;
        private const int NickName = 5;
        private const int MiddleName = 6;
        private const int LastName = 7;
        private const int Suffix = 8;
        private const int FormerName = 9;
        private const int FamilyRole = 10;
        private const int MaritalStatus = 11;
        private const int ConnectionStatus = 12;
        private const int RecordStatus = 13;
        private const int HomePhone = 14;
        private const int MobilePhone = 15;
        private const int WorkPhone = 16;
        private const int Email = 17;
        private const int SecondaryEmail = 18;
        private const int IsEmailActive = 19;
        private const int AllowSMS = 20;
        private const int AllowBulkEmail = 21;
        private const int Gender = 22;
        private const int Age = 23;
        private const int DateOfBirth = 24;
        private const int MembershipDate = 25;
        private const int SalvationDate = 26;
        private const int BaptismDate = 27;
        private const int Anniversary = 28;
        private const int FirstVisit = 29;
        private const int LastUpdated = 30;
        private const int PreviousChurch = 31;
        private const int Occupation = 32;
        private const int Employer = 33;
        private const int School = 34;
        private const int GeneralNote = 35;
        private const int MedicalNote = 36;
        private const int SecurityNote = 37;

        #endregion
    }
}
