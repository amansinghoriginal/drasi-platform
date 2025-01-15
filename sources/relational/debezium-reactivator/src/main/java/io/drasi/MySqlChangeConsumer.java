/*
 * Copyright 2024 The Drasi Authors.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

package io.drasi;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import com.fasterxml.jackson.databind.JsonNode;

import io.drasi.models.RelationalGraphMapping;
import io.drasi.source.sdk.ChangePublisher;
 
 public class MySqlChangeConsumer extends RelationalChangeConsumer {
    private static final Logger log = LoggerFactory.getLogger(RelationalChangeConsumer.class);
 
     public MySqlChangeConsumer(RelationalGraphMapping mappings, ChangePublisher changePublisher) {
         super(mappings, changePublisher);
     }

     @Override
     protected long ExtractLsn(JsonNode sourceChange) {
        // Get binlog position which is always present
        log.info("[AMAN] Getting binlog position which is always present.");
        long position = sourceChange.path("pos").asLong(0);
        log.info("[AMAN] Position: " + position);
        
        // Get binlog file number from filename (ex: "mysql-bin.000003")
        log.info("[AMAN] Getting binlog file number from filename.");
        String binlogFile = sourceChange.path("file").asText("");
        long fileNumber = 0;
        if (!binlogFile.isEmpty()) {
            try {
                // Extract number from end of filename
                String numberPart = binlogFile.substring(binlogFile.lastIndexOf(".") + 1);
                fileNumber = Long.parseLong(numberPart);
            } catch (NumberFormatException e) {
                // If parsing fails, use 0
                log.info("[AMAN] Error parsing binlog file number: " + e.getMessage());
            }
        }
        log.info("[AMAN] File number: " + fileNumber);
        
        // Combine file number and position into single LSN.
        // Binlog file numbers have a specific format:
        // they're 6-digit numbers padded with zeroes (e.g., "mysql-bin.000001").
        // Thus, atmost they will need 20 bits. They are always increasing, and
        // to maintain replication order, we can give them the higher 20 bits.
        long lsn = (fileNumber << 44) | position;
        log.info("[AMAN] LSN: " + lsn);
        return lsn;
     }
 }
 