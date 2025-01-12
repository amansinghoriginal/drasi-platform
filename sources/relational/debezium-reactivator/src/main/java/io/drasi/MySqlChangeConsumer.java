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

 import com.fasterxml.jackson.databind.JsonNode;
 import io.drasi.models.RelationalGraphMapping;
 import io.drasi.source.sdk.ChangePublisher;
 
 public class MySqlChangeConsumer extends RelationalChangeConsumer {
 
     public MySqlChangeConsumer(RelationalGraphMapping mappings, ChangePublisher changePublisher) {
         super(mappings, changePublisher);
     }
 
     @Override
     protected long ExtractLsn(JsonNode sourceChange) {
        // Get binlog position which is always present
        long position = sourceChange.path("pos").asLong(0);
        
        // Get binlog file number from filename (e.g., "mysql-bin.000003")
        String binlogFile = sourceChange.path("file").asText("");
        long fileNumber = 0;
        if (!binlogFile.isEmpty()) {
            try {
                // Extract number from end of filename
                String numberPart = binlogFile.substring(binlogFile.lastIndexOf(".") + 1);
                fileNumber = Long.parseLong(numberPart);
            } catch (Exception e) {
                // If parsing fails, use 0
            }
        }
        
        // Combine file number and position into single LSN
        // Use file number as high bits and position as low bits
        return (fileNumber << 32) | position;
     }
 }
 