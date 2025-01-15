package io.drasi;

import io.drasi.models.RelationalGraphMapping;
import io.debezium.config.Configuration;
import io.debezium.engine.ChangeEvent;
import io.debezium.engine.DebeziumEngine;
import io.debezium.engine.format.Json;
import io.debezium.engine.spi.OffsetCommitPolicy;
import io.drasi.source.sdk.ChangeMonitor;
import io.drasi.source.sdk.ChangePublisher;
import io.drasi.source.sdk.Reactivator;
import io.drasi.source.sdk.StateStore;

import java.util.Properties;

public class MySqlChangeMonitor implements ChangeMonitor {
    private DebeziumEngine<ChangeEvent<String, String>> engine;

    public MySqlChangeMonitor() {
    }

    @Override
    public void run(ChangePublisher changePublisher, StateStore stateStore) throws Exception {
        var sourceId = Reactivator.SourceId();
        var tableListStr = Reactivator.GetConfigValue("tables");
        var tableList = tableListStr.split(",");

        Configuration config = Configuration.create()
                .with("connector.class", "io.debezium.connector.mysql.MySqlConnector")
                .with("offset.storage", "io.drasi.OffsetBackingStore")
                .with("offset.flush.interval.ms", 5000)
                .with("name", sourceId)
                .with("topic.prefix", sourceId)
                .with("database.server.name", sourceId)
                .with("database.hostname", Reactivator.GetConfigValue("host"))
                .with("database.port", Reactivator.GetConfigValue("port"))
                .with("database.user", Reactivator.GetConfigValue("user"))
                .with("database.password", Reactivator.GetConfigValue("password"))
                .with("database.include.list", Reactivator.GetConfigValue("database"))
                .with("database.server.id", "1")
                .with("table.include.list", tableListStr)
                .with("schema.history.internal", "io.drasi.NoOpSchemaHistory")
                .with("snapshot.mode", "never")
                .with("tombstones.on.delete", false)
                .with("decimal.handling.mode", "double")
                .with("time.precision.mode", "adaptive_time_microseconds")
                .with("converters", "temporalConverter")
                .with("temporalConverter.type", "io.drasi.TemporalConverter")
                .build();

        var sr = new SchemaReader(config);
        var mappings = new RelationalGraphMapping();
        mappings.nodes = sr.ReadMappingsFromSchema(tableList);

        final Properties props = config.asProperties();

        engine = DebeziumEngine.create(Json.class)
                .using(props)
                .using(OffsetCommitPolicy.always())
                .using((success, message, error) -> {
                    if (!success && error != null) {
                        Reactivator.TerminalError(error);
                    }
                })
                .notifying(new MySqlChangeConsumer(mappings, changePublisher))
                .build();
        
        engine.run();
    }

    public void close() throws Exception {
        engine.close();
    }
}