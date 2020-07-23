# SmartBatteryHack
This is a hacking tool for smart batteries using SMBus. Originally written for a Dell J1KND battery that uses a [BQ8050](Datasheets/BQ8050_datasheet.pdf) fuel gauge IC. The Sanyo firmware in it is hard to understand.

**Arduino** folder contains the source code for an Uno/Mega which acts as an interface between the battery and an external computer. The I2C pins are connected to the smart battery's SDA/SCL pins with pullup resistors (4.7kOhm to 5V). The battery's ground pin has to be connected to the Uno/Mega's ground pin. Additionally the battery's "system present" pin may need to be grounded too (through a 100-1000 Ohm resistor). You need to install the SoftI2CMaster library to be able to use this sketch.

**GUI** folder contains the C# source code and compiled binary for an external computer (Windows 7 and up) written in Visual Studio 2019.

![GUI 01](https://boundaryconditionhome.files.wordpress.com/2020/01/sbhack_gui_01.png)

![GUI 02](https://boundaryconditionhome.files.wordpress.com/2020/01/sbhack_gui_02.png)

![Battery connection](https://boundaryconditionhome.files.wordpress.com/2020/02/img_20200202_092224_02.jpg)

More info about this project:  
https://boundarycondition.home.blog/2020/01/18/the-repairing-and-hacking-of-a-dell-j1knd-bq8050-laptop-battery/