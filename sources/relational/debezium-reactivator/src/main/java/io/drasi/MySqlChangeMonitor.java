package io.drasi;

import io.debezium.config.Configuration;
import io.debezium.engine.ChangeEvent;
import io.debezium.engine.DebeziumEngine;
import io.debezium.engine.format.Json;
import io.debezium.engine.spi.OffsetCommitPolicy;
import io.drasi.source.sdk.ChangeMonitor;
import io.drasi.source.sdk.ChangePublisher;
import io.drasi.source.sdk.Reactivator;
import io.drasi.source.sdk.StateStore;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.util.Properties;

public class MySqlChangeMonitor implements ChangeMonitor {
    private static final Logger log = LoggerFactory.getLogger(MySqlChangeMonitor.class);
    private DebeziumEngine<ChangeEvent<String, String>> engine;

    public MySqlChangeMonitor() {
        log.info("[AMAN] MySqlChangeMonitor instance created.");
    }

    @Override
    public void run(ChangePublisher changePublisher, StateStore stateStore) throws Exception {
        log.info("[AMAN] MySqlChangeMonitor run method invoked.");

        var sourceId = Reactivator.SourceId();
        var tableListStr = Reactivator.GetConfigValue("tables");
        var tableList = tableListStr.split(",");

        for (String string : tableList) {
            log.info("[AMAN] table entry in tableList = {}", string);
        }

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

        final Properties props = config.asProperties();
        log.info("[AMAN] Configuration properties:");
        for (var entry : props.entrySet()) {
            log.info("\t....[AMAN] Property: {} = {}", entry.getKey(), entry.getValue());
        }

        engine = DebeziumEngine.create(Json.class)
                .using(props)
                .using(OffsetCommitPolicy.always())
                .notifying(record -> {
                    if (record.value() != null) {
                        log.info("[AMAN] Change detected: Key = {}, Value = {}", record.key(), record.value());
                    } else {
                        log.info("[AMAN] Heartbeat or schema change event received.");
                    }
                })
                .using((success, message, error) -> {
                    if (!success && error != null) {
                        log.error("[AMAN] Engine error: {}", error.getMessage(), error);
                        // Log the full stack trace
                        log.error("[AMAN] Full error: ", error);
                    } else if (!success) {
                        log.error("[AMAN] Engine failed without error: {}", message);
                    } else if (success) {
                        log.info("[AMAN] Engine completed successfully: {}", message);
                    }
                })
                .build();

        log.info("[AMAN] Starting Debezium engine for MySQL...");
        engine.run();
    }

    public void close() throws Exception {
        if (engine != null) {
            log.info("[AMAN] Stopping Debezium engine for MySQL...");
            engine.close();
            log.info("[AMAN] Debezium engine stopped.");
        }
    }
}
