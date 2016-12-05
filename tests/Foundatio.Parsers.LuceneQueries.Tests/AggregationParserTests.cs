﻿using System;
using System.Collections.Generic;
using Foundatio.Logging;
using Foundatio.Logging.Xunit;
using Foundatio.Parsers.ElasticQueries;
using Foundatio.Parsers.ElasticQueries.Extensions;
using Foundatio.Utility;
using Nest;
using Xunit;
using Xunit.Abstractions;

namespace Foundatio.Parsers.Tests {
    public class AggregationParserTests : TestWithLoggingBase {
        public AggregationParserTests(ITestOutputHelper output) : base(output) { }

        private IElasticClient GetClient(ConnectionSettings settings = null) {
            if (settings == null)
                settings = new ConnectionSettings();

            return new ElasticClient(settings.DisableDirectStreaming().PrettyJson());
        }

        [Fact]
        public void ProcessAggregations() {
            var client = GetClient();
            client.DeleteIndex("stuff");
            client.Refresh("stuff");

            client.CreateIndex("stuff");
            client.Map<MyType>(d => d.Dynamic(true).Index("stuff").Properties(p => p.GeoPoint(g => g.Name(f => f.Field3))));
            var res = client.IndexMany(new List<MyType> {
                new MyType { Field1 = "value1", Field4 = 1, Field3 = "51.5032520,-0.1278990", Field5 = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(5)), Field2 = "field2" },
                new MyType { Field1 = "value2", Field4 = 2, Field3 = "51.5032520,-0.1278990", Field5 = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(4)) },
                new MyType { Field1 = "value3", Field4 = 3, Field3 = "51.5032520,-0.1278990", Field5 = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(3)) },
                new MyType { Field1 = "value4", Field4 = 4, Field3 = "51.5032520,-0.1278990", Field5 = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(2)) },
                new MyType { Field1 = "value5", Field4 = 5, Field3 = "51.5032520,-0.1278990", Field5 = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(1)) }
            }, "stuff");
            client.Refresh("stuff");

            var processor = new ElasticQueryParser(c => c.UseMappings<MyType>(client, "stuff").UseGeo(l => "51.5032520,-0.1278990"));
            var aggregations = processor.BuildAggregationsAsync("min:field4 max:field4 avg:field4 sum:field4 percentiles:field4 cardinality:field4 missing:field2 date:field5 geogrid:field3 terms:field1").Result;

            var actualResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(aggregations));
            string actualRequest = actualResponse.GetRequest();
            _logger.Info($"Actual: {actualRequest}");

            var expectedResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(a => a
                .GeoHash("geogrid_field3", h => h.Field("field3").GeoHashPrecision(GeoHashPrecision.Precision1)
                    .Aggregations(a1 => a1.Average("avg_lat", s => s.Script(ss => ss.Inline("doc['field3'].lat"))).Average("avg_lon", s => s.Script(ss => ss.Inline("doc['field3'].lon")))))
                .Terms("terms_field1", t => t.Field("field1.keyword"))
                .DateHistogram("date_field5", d1 => d1.Field("field5").Interval("1d").Format("date_optional_time"))
                .Missing("missing_field2", t => t.Field("field2.keyword"))
                .Cardinality("cardinality_field4", c => c.Field("field4"))
                .Percentiles("percentiles_field4", c => c.Field("field4"))
                .Sum("sum_field4", c => c.Field("field4"))
                .Average("avg_field4", c => c.Field("field4"))
                .Max("max_field4", c => c.Field("field4"))
                .Min("min_field4", c => c.Field("field4"))));
            string expectedRequest = expectedResponse.GetRequest();
            _logger.Info($"Expected: {expectedRequest}");

            Assert.Equal(expectedRequest, actualRequest);
            Assert.True(actualResponse.IsValid);
            Assert.True(expectedResponse.IsValid);
            Assert.Equal(expectedResponse.Total, actualResponse.Total);
        }

        [Fact]
        public void ProcessTermAggregations() {
            var client = GetClient();
            client.DeleteIndex("stuff");
            client.Refresh("stuff");

            client.CreateIndex("stuff");
            client.Map<MyType>(d => d.Dynamic(true).Index("stuff"));
            var res = client.IndexMany(new List<MyType> { new MyType { Field1 = "value1" } }, "stuff");
            client.Refresh("stuff");

            var processor = new ElasticQueryParser(c => c.UseMappings<MyType>(client, "stuff"));
            var aggregations = processor.BuildAggregationsAsync("terms:(field1 @exclude:-F @include:myinclude @missing:mymissing @min:1)").Result;

            var actualResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(aggregations));
            string actualRequest = actualResponse.GetRequest();
            _logger.Info($"Actual: {actualRequest}");

            var expectedResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(a => a
                .Terms("terms_field1", t => t
                    .Field("field1.keyword")
                    .MinimumDocumentCount(1)
                    .Include("myinclude")
                    .Exclude("-F")
                    .Missing("mymissing"))));
            string expectedRequest = expectedResponse.GetRequest();
            _logger.Info($"Expected: {expectedRequest}");

            Assert.Equal(expectedRequest, actualRequest);
            Assert.True(actualResponse.IsValid);
            Assert.True(expectedResponse.IsValid);
            Assert.Equal(expectedResponse.Total, actualResponse.Total);
        }

        [Fact]
        public void ProcessSortedTermAggregations() {
            var client = GetClient();
            client.DeleteIndex("stuff");
            client.Refresh("stuff");

            client.CreateIndex("stuff");
            client.Map<MyType>(d => d.Dynamic(true).Index("stuff"));
            var res = client.IndexMany(new List<MyType> { new MyType { Field1 = "value1" } }, "stuff");
            client.Refresh("stuff");

            var processor = new ElasticQueryParser(c => c.UseMappings<MyType>(client, "stuff"));
            var aggregations = processor.BuildAggregationsAsync("terms:(-field1) terms:+field2").Result;

            var actualResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(aggregations));
            string actualRequest = actualResponse.GetRequest();
            _logger.Info($"Actual: {actualRequest}");

            var expectedResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(a => a
                .Terms("terms_field1", t => t
                    .Field("field1.keyword")
                    .OrderDescending("field1.keyword")
                    .Exclude("1"))
                .Terms("terms_field2", t => t
                    .Field("field2.keyword")
                    .OrderAscending("field2.keyword")
                    .Exclude("1"))));
            string expectedRequest = expectedResponse.GetRequest();
            _logger.Info($"Expected: {expectedRequest}");

            Assert.Equal(expectedRequest, actualRequest);
            Assert.True(actualResponse.IsValid);
            Assert.True(expectedResponse.IsValid);
            Assert.Equal(expectedResponse.Total, actualResponse.Total);
        }

        [Fact]
        public void ProcessDateHistogramAggregations() {
            var client = GetClient();
            client.DeleteIndex("stuff");
            client.Refresh("stuff");

            client.CreateIndex("stuff");
            client.Map<MyType>(d => d.Dynamic(true).Index("stuff"));
            var res = client.IndexMany(new List<MyType> { new MyType { Field5 = SystemClock.UtcNow } }, "stuff");
            client.Refresh("stuff");

            var processor = new ElasticQueryParser(c => c.UseMappings<MyType>(client, "stuff"));
            var aggregations = processor.BuildAggregationsAsync("date:(field5 @missing:\"0001-01-01T00:00:00\")").Result;

            var actualResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(aggregations));
            string actualRequest = actualResponse.GetRequest();
            _logger.Info($"Actual: {actualRequest}");

            var expectedResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(a => a
                .DateHistogram("date_field5", d1 => d1
                    .Field("field5")
                    .Interval("1d")
                    .Format("date_optional_time")
                    .Missing(DateTime.MinValue))));
            string expectedRequest = expectedResponse.GetRequest();
            _logger.Info($"Expected: {expectedRequest}");

            Assert.Equal(expectedRequest, actualRequest);
            Assert.True(actualResponse.IsValid);
            Assert.True(expectedResponse.IsValid);
            Assert.Equal(expectedResponse.Total, actualResponse.Total);
        }


        [Fact]
        public void CanSpecifyDefaultValuesAggregations() {
            var client = GetClient();
            client.DeleteIndex("stuff");
            client.Refresh("stuff");

            client.CreateIndex("stuff");
            client.Map<MyType>(d => d.Dynamic(true).Index("stuff"));
            var res = client.IndexMany(new List<MyType> { new MyType { Field1 = "test" }, new MyType { Field4 = 1 } }, "stuff");
            client.Refresh("stuff");

            var processor = new ElasticQueryParser(c => c.UseMappings<MyType>(client, "stuff"));
            var aggregations = processor.BuildAggregationsAsync("min:(field4 @default:0) max:(field4 @default:0) avg:(field4 @default:0) sum:(field4 @default:0) cardinality:(field4 @default:0)").Result;

            var actualResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(aggregations));
            string actualRequest = actualResponse.GetRequest();
            _logger.Info($"Actual: {actualRequest}");

            const string script = "doc['field4'].empty ? 0 : doc['field4'].value";
            var expectedResponse = client.Search<MyType>(d => d.Index("stuff").Aggregations(a => a
                .Cardinality("cardinality_field4", c => c.Script(script))
                .Sum("sum_field4", c => c.Script(script))
                .Average("avg_field4", c => c.Script(script))
                .Max("max_field4", c => c.Script(script))
                .Min("min_field4", c => c.Script(script))));
            string expectedRequest = expectedResponse.GetRequest();
            _logger.Info($"Expected: {expectedRequest}");

            Assert.Equal(expectedRequest, actualRequest);
            Assert.True(actualResponse.IsValid);
            Assert.True(expectedResponse.IsValid);
            Assert.Equal(expectedResponse.Total, actualResponse.Total);
        }
    }
}