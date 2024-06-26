// ------------------------------------------------------------------------------
// <auto-generated>
//    Generated by avrogen, version 1.11.3
//    Changes to this file may cause incorrect behavior and will be lost if code
//    is regenerated
// </auto-generated>
// ------------------------------------------------------------------------------
namespace GeneratedSchemaTypes
{
	using System;
	using System.Collections.Generic;
	using System.Text;
	using global::Avro;
	using global::Avro.Specific;
	
	[global::System.CodeDom.Compiler.GeneratedCodeAttribute("avrogen", "1.11.3")]
	public partial class Person : global::Avro.Specific.ISpecificRecord
	{
		public static global::Avro.Schema _SCHEMA = global::Avro.Schema.Parse(@"{""type"":""record"",""name"":""Person"",""namespace"":""GeneratedSchemaTypes"",""fields"":[{""name"":""Id"",""type"":""string""},{""name"":""Name"",""type"":{""type"":""record"",""name"":""PersonName"",""namespace"":""GeneratedSchemaTypes"",""fields"":[{""name"":""Given"",""type"":""string""},{""name"":""Family"",""type"":""string""}]}},{""name"":""Tags"",""type"":{""type"":""array"",""items"":""string""}}]}");
		private string _Id;
		private GeneratedSchemaTypes.PersonName _Name;
		private IList<System.String> _Tags;
		public virtual global::Avro.Schema Schema
		{
			get
			{
				return Person._SCHEMA;
			}
		}
		public string Id
		{
			get
			{
				return this._Id;
			}
			set
			{
				this._Id = value;
			}
		}
		public GeneratedSchemaTypes.PersonName Name
		{
			get
			{
				return this._Name;
			}
			set
			{
				this._Name = value;
			}
		}
		public IList<System.String> Tags
		{
			get
			{
				return this._Tags;
			}
			set
			{
				this._Tags = value;
			}
		}
		public virtual object Get(int fieldPos)
		{
			switch (fieldPos)
			{
			case 0: return this.Id;
			case 1: return this.Name;
			case 2: return this.Tags;
			default: throw new global::Avro.AvroRuntimeException("Bad index " + fieldPos + " in Get()");
			};
		}
		public virtual void Put(int fieldPos, object fieldValue)
		{
			switch (fieldPos)
			{
			case 0: this.Id = (System.String)fieldValue; break;
			case 1: this.Name = (GeneratedSchemaTypes.PersonName)fieldValue; break;
			case 2: this.Tags = (IList<System.String>)fieldValue; break;
			default: throw new global::Avro.AvroRuntimeException("Bad index " + fieldPos + " in Put()");
			};
		}
	}
}
