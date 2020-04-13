from smbus2 import SMBus
import time

# Smart Battery Hack class
class SBHack():
    def __init__(self, bus, address):
        self.bus = bus
        self.address = address
        
    def read_byte(self, reg):
        try:
            read = self.bus.read_byte_data(self.address, reg)
            return read
        except:
            return -1
    
    def read_word(self, reg):
        try:
            read = self.bus.read_i2c_block_data(self.address, reg, 2)
            return (read[1] << 8) + read[0] # bytes reverse ordered
        except:
            return -1
        
    def read_block(self, reg, len):
        try:
            read = self.bus.read_i2c_block_data(self.address, reg, len)
            return read
        except:
            return -1
        
    def write_word(self, reg, word):
        data = [(word & 0xff), (word >> 8) & 0xff] # reverse ordered
        try:
            self.bus.write_i2c_block_data(self.address, reg, data)
        except:
            return -1
    
    def operation_status(self):
        operation_status = battery.read_word(0x54)
        print("Operation status: {0:#0{1}x}".format(operation_status,6))
        return operation_status
    
    def unseal(self):
        self.operation_status()
        print("Writing 0x0214 to 0x71")
        battery.write_word(0x71, 0x0214)
        print("Reading random challenge from 0x73 and 0x74")
        rc_01 = battery.read_word(0x73)
        rc_02 = battery.read_word(0x74)
        if (rc_01 > -1) and (rc_02 > -1):
            print("Random challenge received: {0:#0{1}x} ".format(rc_01,6) + "{0:#0{1}x} ".format(rc_02,6))
            solution = 0x10000 - rc_01
            print("Solution: 0x10000-{0:#0{1}x}".format(rc_01,6) + "={0:#0{1}x}".format(solution,6))
            print("Write solution to 0x71")
            battery.write_word(0x70, solution)
            #print("Write 0x0517 to 0x00")
            #battery.write_word(0x00, 0x0517)
            i = 0
            while i < 5:
                a = self.operation_status()
                if a != 0xff0d: break
                time.sleep(0.2)
                i += 1
        else:
            print("No random challenge received!")
    
    def read_reg(self, start, end):
        i = start
        while i < (end+1):
            word = self.read_word(i)
            if word > -1:
                print("{0:#0{1}x}: ".format(i,4) + "{0:#0{1}x}".format(word,6))
            else:
                print("{0:#0{1}x}: n/a".format(i,4))
            i += 1

    def reverseByteOrder(self, data):
        # Reverses the byte order of an int (16-bit) or long (32-bit) value
        # Courtesy Vishal Sapre
        dstr = hex(data)[2:].replace('L','')
        byteCount = len(dstr[::2])
        val = 0
        for i, n in enumerate(range(byteCount)):
            d = data & 0xFF
            val |= (d << (8 * (byteCount - i - 1)))
            data >>= 8
        return val
    
    def readBit(self, reg, bitNum):
        b = self.readU8(reg)
        data = b & (1 << bitNum)
        return data
    
    def writeBit(self, reg, bitNum, data):
        b = self.readU8(reg)
        
        if data != 0:
            b = (b | (1 << bitNum))
        else:
            b = (b & ~(1 << bitNum))
            
        return self.write8(reg, b)
    
    def readBits(self, reg, bitStart, length):
        # 01101001 read byte
        # 76543210 bit numbers
        #    xxx   args: bitStart=4, length=3
        #    010   masked
        #   -> 010 shifted  
        
        b = self.readU8(reg)
        mask = ((1 << length) - 1) << (bitStart - length + 1)
        b &= mask
        b >>= (bitStart - length + 1)
        
        return b
        
    
    def writeBits(self, reg, bitStart, length, data):
        #      010 value to write
        # 76543210 bit numbers
        #    xxx   args: bitStart=4, length=3
        # 00011100 mask byte
        # 10101111 original value (sample)
        # 10100011 original & ~mask
        # 10101011 masked | value
        
        b = self.readU8(reg)
        mask = ((1 << length) - 1) << (bitStart - length + 1)
        data <<= (bitStart - length + 1)
        data &= mask
        b &= ~(mask)
        b |= data
            
        return self.write8(reg, b)

    def readBytes(self, reg, length):
        output = []
        
        i = 0
        while i < length:
            output.append(self.readU8(reg))
            i += 1
            
        return output        
        
    def readBytesListU(self, reg, length):
        output = []
        
        i = 0
        while i < length:
            output.append(self.readU8(reg + i))
            i += 1
            
        return output

    def readBytesListS(self, reg, length):
        output = []
        
        i = 0
        while i < length:
            output.append(self.readS8(reg + i))
            i += 1
            
        return output        
    
    def writeList(self, reg, list):
        # Writes an array of bytes using I2C format"
        try:
            self.bus.write_i2c_block_data(self.address, reg, list)
        except (IOError):
            print ("Error accessing 0x%02X: Check your I2C address" % self.address)
        return -1    
    
    def write8(self, reg, value):
        # Writes an 8-bit value to the specified register/address
        try:
            self.bus.write_byte_data(self.address, reg, value)
        except (IOError):
            print ("Error accessing 0x%02X: Check your I2C address" % self.address)
            return -1

    def readU8(self, reg):
        # Read an unsigned byte from the I2C device
        try:
            result = self.bus.read_byte_data(self.address, reg)
            return result
        except (IOError):
            print ("Error accessing 0x%02X: Check your I2C address" % self.address)
            return -1

    def readS8(self, reg):
        # Reads a signed byte from the I2C device
        try:
            result = self.bus.read_byte_data(self.address, reg)
            if result > 127:
                return result - 256
            else:
                return result
        except (IOError):
            print ("Error accessing 0x%02X: Check your I2C address" % self.address)
            return -1

    def readU16(self, reg):
        # Reads an unsigned 16-bit value from the I2C device
        try:
            hibyte = self.bus.read_byte_data(self.address, reg)
            result = (hibyte << 8) + self.bus.read_byte_data(self.address, reg + 1)
            return result
        except (IOError):
            print ("Error accessing 0x%02X: Check your I2C address" % self.address)
            return -1

    def readS16(self, reg):
        # Reads a signed 16-bit value from the I2C device
        try:
            hibyte = self.bus.read_byte_data(self.address, reg)
            if hibyte > 127:
                hibyte -= 256
            result = (hibyte << 8) + self.bus.read_byte_data(self.address, reg + 1)
            return result
        except (IOError):
            print ("Error accessing 0x%02X: Check your I2C address" % self.address)
            return -1

battery = SBHack(SMBus(1), 0x0b)

#battery.operation_status()
battery.read_reg(0x00, 0xff)

'''
i = 0
while i < 32:
    battery.write_word(0x00, i)
    read = battery.read_word(0x23)
    print("{0:#0{1}x}: ".format(i,6) + "{0:#0{1}x}".format(read,6))
    i += 1
'''

battery.write_word(0x71, 0x0214)
read = battery.read_word(0x23)
print("{0:#0{1}x}".format(read,6))